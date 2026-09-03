// Remembers which student is selected in the nav-bar "Viewing as" switcher, per browser.
window.campusPulseUser = {
    get(key) {
        try {
            return window.localStorage.getItem(key);
        } catch {
            return null;
        }
    },
    set(key, value) {
        try {
            window.localStorage.setItem(key, value);
        } catch {
            /* storage unavailable - selection just won't persist */
        }
    },
    remove(key) {
        try {
            window.localStorage.removeItem(key);
        } catch {
            /* no-op */
        }
    }
};
