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
    registerComposer: function (textareaId, buttonId) {
        const textarea = document.getElementById(textareaId);
        const button = document.getElementById(buttonId);

        if (!textarea || !button || textarea.dataset.lkComposerBound === "true") {
            return;
        }

        textarea.dataset.lkComposerBound = "true";
        textarea.addEventListener("keydown", function (event) {
            if (event.key === "Enter" && !event.shiftKey) {
                event.preventDefault();
                button.click();
            }
        });
    }
};
