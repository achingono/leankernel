window.leanKernelChat = window.leanKernelChat || {
    storage: {
        get: function (key) {
            try {
                return window.localStorage.getItem(key);
            } catch {
                return null;
            }
        },
        set: function (key, value) {
            try {
                window.localStorage.setItem(key, value);
            } catch {
                // Ignore browser storage failures and keep the UI usable.
            }
        },
        remove: function (key) {
            try {
                window.localStorage.removeItem(key);
            } catch {
                // Ignore browser storage failures and keep the UI usable.
            }
        }
    },
    scrollToBottom: function (elementId) {
        const element = document.getElementById(elementId);
        if (!element) {
            return;
        }

        window.requestAnimationFrame(() => {
            element.scrollTop = element.scrollHeight;
        });
    },
    getTextareaValue: function (elementId) {
        const el = document.getElementById(elementId);
        if (!el) {
            return "";
        }

        const innerTextarea = el.shadowRoot?.querySelector("textarea");
        if (innerTextarea) {
            return innerTextarea.value ?? "";
        }

        // Fallback for non-shadow textarea implementations.
        return el.value ?? "";
    },
    registerComposer: function (textareaId, buttonId) {
        const textarea = document.getElementById(textareaId);
        const button = document.getElementById(buttonId);

        if (!textarea || !button) {
            return;
        }

        const tryBind = function () {
            const innerTextarea = textarea.shadowRoot?.querySelector("textarea");
            if (!innerTextarea) {
                return false;
            }

            if (innerTextarea.dataset.lkComposerBound === "true") {
                return true;
            }

            innerTextarea.addEventListener("keydown", function (event) {
                if (event.key !== "Enter" || event.shiftKey || event.isComposing) {
                    return;
                }

                event.preventDefault();
                if (!button.hasAttribute("disabled")) {
                    button.click();
                }
            });

            const updateSendDisabled = function () {
                const value = innerTextarea.value ?? "";
                const isDisabled = value.trim().length === 0;
                if (isDisabled) {
                    button.setAttribute("disabled", "disabled");
                } else {
                    button.removeAttribute("disabled");
                }
            };

            innerTextarea.addEventListener("input", updateSendDisabled);
            updateSendDisabled();

            innerTextarea.dataset.lkComposerBound = "true";
            return true;
        };

        // The shadow textarea may not exist yet; retry a few frames until it does.
        let attempts = 0;
        const maxAttempts = 10;
        const schedule = function () {
            attempts++;
            const bound = tryBind();
            if (bound) {
                return;
            }

            if (attempts < maxAttempts) {
                window.requestAnimationFrame(schedule);
            }
        };

        schedule();
    }
};
