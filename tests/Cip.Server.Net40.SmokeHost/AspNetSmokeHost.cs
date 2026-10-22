using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web;
using System.Web.Hosting;
using Cip.Server.Net40.Configuration;
using Cip.Server.Net40.Domain;
using Cip.Server.Net40.Infrastructure;

namespace Cip.Server.Net40.SmokeHost
{
    [Serializable]
    public sealed class SmokeRequest
    {
        public string Method { get; set; }
        public string Path { get; set; }
        public string Query { get; set; }
        public string ContentType { get; set; }
        public string Body { get; set; }
        public string Cookie { get; set; }
    }

    [Serializable]
    public sealed class SmokeResponse
    {
        public int StatusCode { get; set; }
        public string StatusDescription { get; set; }
        public string Body { get; set; }
        public IDictionary<string, string> Headers { get; set; }
    }

    [Serializable]
    public sealed class CandidateInfo
    {
        public int Id { get; set; }
        public string PassengerName { get; set; }
        public string FlightCode { get; set; }
    }

    public sealed class AspNetSmokeHost : MarshalByRefObject
    {
        private readonly HashSet<int> _cleanupAllowedIds = new HashSet<int>();

        public override object InitializeLifetimeService()
        {
            return null;
        }

        public SmokeResponse Process(SmokeRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            StringWriter output = new StringWriter(CultureInfo.InvariantCulture);
            CapturingWorkerRequest worker = new CapturingWorkerRequest(request, output);
            HttpRuntime.ProcessRequest(worker);
            return new SmokeResponse
            {
                StatusCode = worker.StatusCode,
                StatusDescription = worker.StatusDescription,
                Body = output.ToString(),
                Headers = worker.ResponseHeaders
            };
        }

        public IList<CandidateInfo> PrepareCandidates(int count)
        {
            LegacySettings settings = new LegacySettings();
            DatabaseConnectionFactory factory = new DatabaseConnectionFactory(settings);
            SourcePassengerRepository source = new SourcePassengerRepository(factory, settings);
            ConsentRepository consent = new ConsentRepository(factory, settings);
            IList<SourcePassenger> passengers = source.GetLatest();
            List<int> ids = new List<int>();
            foreach (SourcePassenger passenger in passengers)
            {
                ids.Add(passenger.Id);
            }

            IDictionary<int, DpoConsentState> states = consent.GetStates(ids);
            List<CandidateInfo> candidates = new List<CandidateInfo>();
            foreach (SourcePassenger passenger in passengers)
            {
                if (!states.ContainsKey(passenger.Id))
                {
                    _cleanupAllowedIds.Add(passenger.Id);
                    candidates.Add(new CandidateInfo
                    {
                        Id = passenger.Id,
                        PassengerName = passenger.PassengerName,
                        FlightCode = passenger.FlightCode
                    });
                    if (candidates.Count >= count)
                    {
                        break;
                    }
                }
            }

            return candidates;
        }

        public int CleanupCreatedRows(int[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                return 0;
            }

            LegacySettings settings = new LegacySettings();
            DatabaseConnectionFactory factory = new DatabaseConnectionFactory(settings);
            int deleted = 0;
            using (SqlConnection connection = factory.CreateConsentConnection())
            {
                connection.Open();
                foreach (int id in ids)
                {
                    if (!_cleanupAllowedIds.Contains(id))
                    {
                        throw new InvalidOperationException("Từ chối cleanup ID không được xác minh trước test: " + id);
                    }

                    using (SqlCommand command = new SqlCommand("DELETE FROM dbo.DPO_CIP WHERE ID = @Id;", connection))
                    {
                        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                        deleted += command.ExecuteNonQuery();
                    }
                }
            }

            return deleted;
        }

        public int CountConsentRows()
        {
            LegacySettings settings = new LegacySettings();
            DatabaseConnectionFactory factory = new DatabaseConnectionFactory(settings);
            using (SqlConnection connection = factory.CreateConsentConnection())
            using (SqlCommand command = new SqlCommand("SELECT COUNT(*) FROM dbo.DPO_CIP;", connection))
            {
                connection.Open();
                return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }
    }

    internal sealed class CapturingWorkerRequest : SimpleWorkerRequest
    {
        private readonly SmokeRequest _request;
        private readonly byte[] _body;
        private readonly IDictionary<string, string> _responseHeaders =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public CapturingWorkerRequest(SmokeRequest request, TextWriter output)
            : base(NormalizePath(request.Path), request.Query ?? string.Empty, output)
        {
            _request = request;
            _body = Encoding.UTF8.GetBytes(request.Body ?? string.Empty);
            StatusCode = 200;
            StatusDescription = "OK";
        }

        public int StatusCode { get; private set; }
        public string StatusDescription { get; private set; }
        public IDictionary<string, string> ResponseHeaders { get { return _responseHeaders; } }

        public override string GetHttpVerbName()
        {
            return string.IsNullOrWhiteSpace(_request.Method) ? "GET" : _request.Method.ToUpperInvariant();
        }

        public override string GetKnownRequestHeader(int index)
        {
            if (index == HeaderContentType)
            {
                return _request.ContentType;
            }

            if (index == HeaderContentLength)
            {
                return _body.Length.ToString(CultureInfo.InvariantCulture);
            }

            if (index == HeaderCookie)
            {
                return _request.Cookie;
            }

            return base.GetKnownRequestHeader(index);
        }

        public override byte[] GetPreloadedEntityBody()
        {
            return _body;
        }

        public override int GetPreloadedEntityBodyLength()
        {
            return _body.Length;
        }

        public override int GetTotalEntityBodyLength()
        {
            return _body.Length;
        }

        public override bool IsEntireEntityBodyIsPreloaded()
        {
            return true;
        }

        public override void SendStatus(int statusCode, string statusDescription)
        {
            StatusCode = statusCode;
            StatusDescription = statusDescription;
            base.SendStatus(statusCode, statusDescription);
        }

        public override void SendKnownResponseHeader(int index, string value)
        {
            string name = GetKnownResponseHeaderName(index) ?? ("Known-" + index);
            AddResponseHeader(name, value);
            base.SendKnownResponseHeader(index, value);
        }

        public override void SendUnknownResponseHeader(string name, string value)
        {
            AddResponseHeader(name, value);
            base.SendUnknownResponseHeader(name, value);
        }

        private void AddResponseHeader(string name, string value)
        {
            string existing;
            _responseHeaders[name] = _responseHeaders.TryGetValue(name, out existing)
                ? existing + ", " + value
                : value;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path == "/")
            {
                return string.Empty;
            }

            return path.TrimStart('/');
        }
    }
}
