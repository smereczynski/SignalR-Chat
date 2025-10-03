$(function () {
    $('ul#users-list').on('click', 'li', function () {
        var username = $(this).data("username");
        var input = $('#message-input');

        var text = input.val();
        if (text.startsWith("/")) {
            text = text.split(")")[1];
        }

        text = "/private(" + username + ") " + text.trim();
        input.val(text);
        input.change();
        input.focus();
    });

    // Emoji UI removed

    $("#expand-sidebar").click(function () {
        $(".sidebar").toggleClass("open");
        $(".users-container").removeClass("open");
    });

    $("#expand-users-list").click(function () {
        $(".users-container").toggleClass("open");
        $(".sidebar").removeClass("open");
    });

    $(document).on("click", ".sidebar.open ul li a, #users-list li", function () {
        $(".sidebar, .users-container").removeClass("open");
    });

    $(".modal").on("shown.bs.modal", function () {
        $(this).find("input[type=text]:first-child").focus();
    });

    $('.modal').on('hidden.bs.modal', function () {
        $(".modal-body input:not(#newRoomName)").val("");
    });

    $(".alert .btn-close").on('click', function () {
        $(this).parent().hide();
    });

    $('body').tooltip({
        selector: '[data-bs-toggle="tooltip"]',
        delay: { show: 500 }
    });

    $("#remove-message-modal").on("shown.bs.modal", function (e) {
        const id = e.relatedTarget.getAttribute('data-messageId');
        $("#itemToDelete").val(id);
    });

    $(document).on("mouseenter", ".ismine", function () {
        $(this).find(".actions").removeClass("d-none");
    });

    $(document).on("mouseleave", ".ismine", function () {
        var isDropdownOpen = $(this).find(".dropdown-menu.show").length > 0;
        if (!isDropdownOpen)
            $(this).find(".actions").addClass("d-none");
    });

    $(document).on("hidden.bs.dropdown", ".actions .dropdown", function () {
        $(this).closest(".actions").addClass("d-none");
    });

    // OTP login/logout handlers
    function postJson(url, data) {
        return fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'same-origin',
            body: JSON.stringify(data || {})
        });
    }

    function setOtpError(msg) {
        var $err = $('#otpError');
        if ($err.length === 0) return;
        if (msg) {
            $err.text(msg).removeClass('d-none');
        } else {
            $err.text('').addClass('d-none');
        }
    }

    $(document).on('click', '#btn-send-otp', function () {
        setOtpError(null);
        var userName = $('#otpUserName').val().trim();
        var destination = ($('#otpDestination').val() || '').trim();
        if (!userName) { setOtpError('Username is required'); return; }
        postJson('/api/auth/start', { userName: userName, destination: destination || null })
            .then(function (r) { if (!r.ok) throw new Error('Failed to send code'); return r.json().catch(function(){return {};}); })
            .then(function () {
                $('#otp-step1').addClass('d-none');
                $('#otp-step2').removeClass('d-none');
                $('#otpCode').focus();
            })
            .catch(function (e) { setOtpError(e.message || 'Error sending code'); });
    });

    $(document).on('click', '#btn-verify-otp', function () {
        setOtpError(null);
        var userName = $('#otpUserName').val().trim();
        var code = $('#otpCode').val().trim();
        if (!userName || !code) { setOtpError('Username and code are required'); return; }
        postJson('/api/auth/verify', { userName: userName, code: code })
            .then(function (r) { if (!r.ok) throw new Error('Invalid code'); return r.json().catch(function(){return {};}); })
            .then(function () {
                var modalEl = document.getElementById('otp-login-modal');
                if (modalEl && window.bootstrap) {
                    try {
                        var modalInstance = bootstrap.Modal.getInstance(modalEl) || new bootstrap.Modal(modalEl);
                        modalInstance.hide();
                    } catch (_) { }
                }
                if (window.chatApp && typeof window.chatApp.onAuthenticated === 'function') {
                    window.chatApp.onAuthenticated();
                } else {
                    // Fallback if chatApp isn't ready
                    console.warn('chatApp.onAuthenticated not available');
                }
            })
            .catch(function (e) { setOtpError(e.message || 'Verification failed'); });
    });

    $(document).on('click', '#btn-logout', function () {
        postJson('/api/auth/logout', {})
            .then(function () {
                if (window.chatApp && typeof window.chatApp.logoutCleanup === 'function') {
                    window.chatApp.logoutCleanup();
                }
            })
            .catch(function () {
                if (window.chatApp && typeof window.chatApp.logoutCleanup === 'function') {
                    window.chatApp.logoutCleanup();
                }
            });
    });

    $('#otp-login-modal').on('show.bs.modal', function(){
        setOtpError(null);
        $('#otp-step1').removeClass('d-none');
        $('#otp-step2').addClass('d-none');
        $('#otpCode').val('');
    });
});