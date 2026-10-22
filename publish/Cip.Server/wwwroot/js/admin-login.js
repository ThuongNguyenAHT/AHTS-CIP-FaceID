(() => {
    'use strict';

    const form = document.getElementById('loginForm');
    const button = document.getElementById('loginButton');
    const error = document.getElementById('loginError');

    form.addEventListener('submit', async event => {
        event.preventDefault();
        button.disabled = true;
        button.textContent = 'Đang đăng nhập…';
        error.classList.add('hidden');

        try {
            const response = await fetch('/api/auth/login', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    username: form.username.value,
                    password: form.password.value
                })
            });

            if (!response.ok) {
                throw new Error(response.status === 401
                    ? 'Tên đăng nhập hoặc mật khẩu không đúng.'
                    : 'Không thể đăng nhập. Vui lòng thử lại.');
            }

            window.location.replace('/admin');
        } catch (exception) {
            error.textContent = exception.message;
            error.classList.remove('hidden');
        } finally {
            button.disabled = false;
            button.textContent = 'Đăng nhập';
        }
    });
})();
