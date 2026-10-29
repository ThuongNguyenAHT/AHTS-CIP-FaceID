using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web;
using System.Web.Routing;
using System.Web.Script.Serialization;
using Cip.Server.Net40.Domain;
using Cip.Server.Net40.Security;

namespace Cip.Server.Net40.Web
{
    internal sealed class LegacyHttpHandler : IHttpHandler
    {
        private readonly LegacyEndpoint _endpoint;
        private readonly RouteData _routeData;

        public LegacyHttpHandler(LegacyEndpoint endpoint, RouteData routeData)
        {
            _endpoint = endpoint;
            _routeData = routeData;
        }

        public bool IsReusable
        {
            get { return false; }
        }

        public void ProcessRequest(HttpContext context)
        {
            try
            {
                Dispatch(context);
            }
            catch (RequestHandledException)
            {
                return;
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceError(exception.ToString());
                WriteJson(context, 500, new
                {
                    message = "Đã xảy ra lỗi trên server.",
                    detail = "Xem log server để biết chi tiết."
                });
            }
        }

        private void Dispatch(HttpContext context)
        {
            switch (_endpoint)
            {
                case LegacyEndpoint.Root:
                    Redirect(context, "/tablet");
                    return;
                case LegacyEndpoint.TabletPage:
                    RequireMethod(context, "GET");
                    WriteAsset(context, "tablet.html", "text/html; charset=utf-8");
                    return;
                case LegacyEndpoint.AdminLoginPage:
                    RequireMethod(context, "GET");
                    WriteAsset(context, "admin-login.html", "text/html; charset=utf-8");
                    return;
                case LegacyEndpoint.AdminPage:
                    RequireMethod(context, "GET");
                    if (!RequirePageAuthentication(context)) return;
                    WriteAsset(context, "admin.html", "text/html; charset=utf-8");
                    return;
                case LegacyEndpoint.SiteCss:
                    RequireMethod(context, "GET");
                    WriteAsset(context, "site.css", "text/css; charset=utf-8");
                    return;
                case LegacyEndpoint.TabletJs:
                    RequireMethod(context, "GET");
                    WriteAsset(context, "tablet.js", "application/javascript; charset=utf-8");
                    return;
                case LegacyEndpoint.TrackingFaceJs:
                    RequireMethod(context, "GET");
                    WriteAsset(context, "tracking-face.min.js", "application/javascript; charset=utf-8");
                    return;
                case LegacyEndpoint.AdminJs:
                    RequireMethod(context, "GET");
                    WriteAsset(context, "admin.js", "application/javascript; charset=utf-8");
                    return;
                case LegacyEndpoint.AdminLoginJs:
                    RequireMethod(context, "GET");
                    WriteAsset(context, "admin-login.js", "application/javascript; charset=utf-8");
                    return;
                case LegacyEndpoint.TabletConfig:
                    HandleTabletConfig(context);
                    return;
                case LegacyEndpoint.TabletCurrent:
                    HandleTabletCurrent(context);
                    return;
                case LegacyEndpoint.TabletBiometric:
                    HandleTabletBiometric(context);
                    return;
                case LegacyEndpoint.TabletDecision:
                    HandleTabletDecision(context);
                    return;
                case LegacyEndpoint.Login:
                    HandleLogin(context);
                    return;
                case LegacyEndpoint.Logout:
                    HandleLogout(context);
                    return;
                case LegacyEndpoint.CurrentUser:
                    HandleCurrentUser(context);
                    return;
                case LegacyEndpoint.Dashboard:
                    HandleDashboard(context);
                    return;
                case LegacyEndpoint.SourcePassengers:
                    HandleSourcePassengers(context);
                    return;
                case LegacyEndpoint.DisplayPassenger:
                    HandleDisplayPassenger(context);
                    return;
                case LegacyEndpoint.SimulateScan:
                    HandleSimulateScan(context);
                    return;
                case LegacyEndpoint.HealthLive:
                    RequireMethod(context, "GET");
                    WriteJson(context, 200, new { status = "Healthy" });
                    return;
                case LegacyEndpoint.HealthReady:
                    HandleHealthReady(context);
                    return;
                default:
                    WriteJson(context, 404, new { message = "Không tìm thấy endpoint." });
                    return;
            }
        }

        private static void HandleTabletConfig(HttpContext context)
        {
            RequireMethod(context, "GET");
            WriteJson(context, 200, new
            {
                fallbackPollingMilliseconds = LegacyServices.Settings.FallbackPollingMilliseconds,
                transport = "PollingOnly"
            });
        }

        private static void HandleTabletCurrent(HttpContext context)
        {
            RequireMethod(context, "GET");
            ConsentDisplay current = LegacyServices.Consents.GetCurrent();
            if (current == null)
            {
                WriteEmpty(context, 204);
                return;
            }

            WriteJson(context, 200, new
            {
                id = current.Id,
                passengerName = current.PassengerName,
                operatingCarrierDesignator = current.OperatingCarrierDesignator,
                flightNumber = current.FlightNumber,
                flightCode = current.FlightCode,
                dateOfFlight = ToIsoString(current.DateOfFlight),
                workstationCode = current.WorkstationCode,
                createdBy = current.CreatedBy,
                policyVersion = current.PolicyVersion,
                policyTitle = current.PolicyTitle,
                policyContent = current.PolicyContent,
                languageCode = current.LanguageCode
            });
        }

        private void HandleTabletBiometric(HttpContext context)
        {
            RequireMethod(context, "POST");
            int id;
            if (!int.TryParse(Convert.ToString(_routeData.Values["id"], CultureInfo.InvariantCulture), out id))
            {
                WriteJson(context, 400, new { message = "ID không hợp lệ." });
                return;
            }

            IDictionary<string, object> body = ReadJsonObject(context);
            string faceImage = GetString(body, "faceImage");
            LegacyServices.Consents.SaveFaceImage(id, faceImage);
            WriteJson(context, 200, new { status = "Saved", id = id });
        }

        private void HandleTabletDecision(HttpContext context)
        {
            RequireMethod(context, "POST");
            int id;
            if (!int.TryParse(Convert.ToString(_routeData.Values["id"], CultureInfo.InvariantCulture), out id))
            {
                WriteJson(context, 400, new { message = "ID không hợp lệ." });
                return;
            }

            IDictionary<string, object> body = ReadJsonObject(context);
            string decision = GetString(body, "decision");
            if (decision != "Accepted" && decision != "Declined" && decision != "NotMe")
            {
                WriteJson(context, 400, new { message = "Decision phải là Accepted, Declined hoặc NotMe." });
                return;
            }

            string faceImage = GetString(body, "faceImage");
            if (decision == "Accepted" && !string.IsNullOrWhiteSpace(faceImage))
            {
                LegacyServices.Consents.SaveFaceImage(id, faceImage);
            }

            DecisionOutcome outcome = decision == "NotMe"
                ? LegacyServices.Consents.CancelPending(id)
                : LegacyServices.Consents.SaveDecision(id, decision == "Accepted");
            if (!outcome.Found)
            {
                WriteJson(context, 404, new { message = "Không tìm thấy phiên xác nhận." });
                return;
            }

            if (!outcome.Applied)
            {
                WriteJson(context, 409, new
                {
                    message = "Phiên xác nhận không còn hiệu lực.",
                    status = outcome.Status
                });
                return;
            }

            WriteJson(context, 200, new
            {
                status = outcome.Status,
                confirmedAt = ToIsoString(outcome.ConfirmedAt)
            });
        }

        private static void HandleLogin(HttpContext context)
        {
            RequireMethod(context, "POST");
            if (!LegacyServices.Credentials.IsConfigured)
            {
                WriteJson(context, 503, new { message = "Chưa cấu hình Admin.Password trên server." });
                return;
            }

            IDictionary<string, object> body = ReadJsonObject(context);
            string username = GetString(body, "username");
            string password = GetString(body, "password");
            if (!LegacyServices.Credentials.Validate(username, password))
            {
                WriteJson(context, 401, new { message = "Tên đăng nhập hoặc mật khẩu không đúng." });
                return;
            }

            LegacyAuthentication.SignIn(context, LegacyServices.Credentials.Username);
            WriteJson(context, 200, new
            {
                username = LegacyServices.Credentials.Username,
                role = "Administrator"
            });
        }

        private static void HandleLogout(HttpContext context)
        {
            RequireMethod(context, "POST");
            string ignored;
            if (!RequireApiAuthentication(context, out ignored)) return;
            LegacyAuthentication.SignOut(context);
            WriteEmpty(context, 204);
        }

        private static void HandleCurrentUser(HttpContext context)
        {
            RequireMethod(context, "GET");
            string username;
            if (!RequireApiAuthentication(context, out username)) return;
            WriteJson(context, 200, new { username = username, role = "Administrator" });
        }

        private static void HandleDashboard(HttpContext context)
        {
            RequireMethod(context, "GET");
            string ignored;
            if (!RequireApiAuthentication(context, out ignored)) return;
            DashboardSummary dashboard = LegacyServices.Consents.GetDashboard();
            WriteJson(context, 200, new
            {
                total = dashboard.Total,
                pending = dashboard.Pending,
                accepted = dashboard.Accepted,
                declined = dashboard.Declined
            });
        }

        private static void HandleSourcePassengers(HttpContext context)
        {
            RequireMethod(context, "GET");
            string ignored;
            if (!RequireApiAuthentication(context, out ignored)) return;

            IList<SourcePassenger> passengers = LegacyServices.SourcePassengers.GetLatest();
            List<int> ids = new List<int>();
            foreach (SourcePassenger passenger in passengers)
            {
                ids.Add(passenger.Id);
            }

            IDictionary<int, DpoConsentState> states = LegacyServices.Consents.GetStates(ids);
            List<object> response = new List<object>();
            foreach (SourcePassenger passenger in passengers)
            {
                DpoConsentState state;
                states.TryGetValue(passenger.Id, out state);
                response.Add(new
                {
                    id = passenger.Id,
                    passengerName = passenger.PassengerName,
                    operatingCarrierDesignator = passenger.OperatingCarrierDesignator,
                    flightNumber = passenger.FlightNumber,
                    flightCode = passenger.FlightCode,
                    dateOfFlight = ToIsoString(passenger.DateOfFlight),
                    workstationCode = passenger.WorkstationCode,
                    warehouseName = passenger.WarehouseName,
                    userName = passenger.UserName,
                    fullName = passenger.FullName,
                    consentStatus = state == null ? "NotSent" : state.Status,
                    confirmedAt = state == null ? null : ToIsoString(state.ConfirmedAt),
                    faceImageUrl = state == null ? LegacyServices.Consents.GetFaceImage(passenger.Id) : (state.FaceImageUrl ?? LegacyServices.Consents.GetFaceImage(passenger.Id))
                });
            }

            WriteJson(context, 200, response);
        }

        private static void HandleSimulateScan(HttpContext context)
        {
            RequireMethod(context, "POST");
            string ignored;
            if (!RequireApiAuthentication(context, out ignored)) return;

            SourcePassenger newPassenger = LegacyServices.SourcePassengers.SimulateNewPassengerScan();
            WriteJson(context, 200, new
            {
                message = "Đã quét thành công vé giả lập mới!",
                passenger = new
                {
                    id = newPassenger.Id,
                    passengerName = newPassenger.PassengerName,
                    flightCode = newPassenger.FlightCode
                }
            });
        }

        private static void HandleDisplayPassenger(HttpContext context)
        {
            RequireMethod(context, "POST");
            string ignored;
            if (!RequireApiAuthentication(context, out ignored)) return;

            IDictionary<string, object> body = ReadJsonObject(context);
            int sourcePassengerId;
            if (!TryGetInt(body, "sourcePassengerId", out sourcePassengerId))
            {
                WriteJson(context, 400, new { message = "SourcePassengerId không hợp lệ." });
                return;
            }

            SourcePassenger passenger = LegacyServices.SourcePassengers.GetById(sourcePassengerId);
            if (passenger == null)
            {
                WriteJson(context, 404, new { message = "Không tìm thấy hành khách trong Database số 1." });
                return;
            }

            QueueForDisplayResult result;
            try
            {
                result = LegacyServices.Consents.QueueForDisplay(passenger);
            }
            catch (InvalidDataException exception)
            {
                WriteJson(context, 400, new { message = exception.Message });
                return;
            }

            if (result == QueueForDisplayResult.AlreadyCompleted)
            {
                WriteJson(context, 409, new { message = "Hành khách này đã xác nhận trước đó." });
                return;
            }

            if (result == QueueForDisplayResult.AnotherPassengerPending)
            {
                WriteJson(context, 409, new
                {
                    message = "Tablet đang hiển thị một hành khách khác. Vui lòng hoàn tất lượt hiện tại trước."
                });
                return;
            }

            WriteJson(context, 200, new
            {
                status = result == QueueForDisplayResult.Queued ? "Queued" : "AlreadyPending",
                sourcePassengerId = passenger.Id,
                notificationTransport = "PollingOnly"
            });
        }

        private static void HandleHealthReady(HttpContext context)
        {
            RequireMethod(context, "GET");
            bool vendorReady = LegacyServices.SourcePassengers.CanConnect();
            bool consentReady = LegacyServices.Consents.CanConnect();
            WriteJson(context, vendorReady && consentReady ? 200 : 503, new
            {
                status = vendorReady && consentReady ? "Healthy" : "Unhealthy",
                vendorDatabase = vendorReady,
                consentDatabase = consentReady
            });
        }

        private static bool RequirePageAuthentication(HttpContext context)
        {
            string ignored;
            if (LegacyAuthentication.IsAuthenticated(context, out ignored))
            {
                return true;
            }

            Redirect(context, "/admin/login");
            return false;
        }

        private static bool RequireApiAuthentication(HttpContext context, out string username)
        {
            if (LegacyAuthentication.IsAuthenticated(context, out username))
            {
                return true;
            }

            WriteJson(context, 401, new { message = "Phiên đăng nhập không hợp lệ hoặc đã hết hạn." });
            return false;
        }

        private static void RequireMethod(HttpContext context, string expectedMethod)
        {
            if (!string.Equals(context.Request.HttpMethod, expectedMethod, StringComparison.OrdinalIgnoreCase))
            {
                WriteJson(context, 405, new { message = "HTTP method không được hỗ trợ." });
                throw new RequestHandledException();
            }
        }

        private static IDictionary<string, object> ReadJsonObject(HttpContext context)
        {
            try
            {
                using (StreamReader reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                {
                    JavaScriptSerializer serializer = CreateSerializer();
                    IDictionary<string, object> result = serializer.DeserializeObject(reader.ReadToEnd()) as IDictionary<string, object>;
                    if (result == null)
                    {
                        throw new InvalidOperationException();
                    }

                    return result;
                }
            }
            catch
            {
                WriteJson(context, 400, new { message = "JSON request không hợp lệ." });
                throw new RequestHandledException();
            }
        }

        private static string GetString(IDictionary<string, object> body, string key)
        {
            object value;
            return body.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
        }

        private static bool TryGetInt(IDictionary<string, object> body, string key, out int value)
        {
            object raw;
            if (!body.TryGetValue(key, out raw) || raw == null)
            {
                value = 0;
                return false;
            }

            try
            {
                value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                return value > 0;
            }
            catch
            {
                value = 0;
                return false;
            }
        }

        private static string ToIsoString(DateTime? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            return DateTime.SpecifyKind(value.Value, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture);
        }

        private static void WriteAsset(HttpContext context, string assetName, string contentType)
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = contentType;
            context.Response.ContentEncoding = Encoding.UTF8;
            context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
            context.Response.Write(AssetProvider.Get(assetName));
        }

        private static void WriteJson(HttpContext context, int statusCode, object value)
        {
            context.Response.Clear();
            context.Response.StatusCode = statusCode;
            context.Response.TrySkipIisCustomErrors = true;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentEncoding = Encoding.UTF8;
            context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
            context.Response.Cache.SetNoStore();
            context.Response.Write(CreateSerializer().Serialize(value));
        }

        private static void WriteEmpty(HttpContext context, int statusCode)
        {
            context.Response.Clear();
            context.Response.StatusCode = statusCode;
            context.Response.TrySkipIisCustomErrors = true;
            context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
            context.Response.Cache.SetNoStore();
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
        }

        private static void Redirect(HttpContext context, string location)
        {
            context.Response.Redirect(location, false);
            context.ApplicationInstance.CompleteRequest();
        }

        private sealed class RequestHandledException : Exception
        {
        }
    }
}
