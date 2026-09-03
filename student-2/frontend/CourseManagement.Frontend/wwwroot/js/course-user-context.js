window.campusPulseCourseUser = {
    getSelectedUserId(key) {
        return window.localStorage.getItem(key);
    },
    setSelectedUserId(key, userId) {
        window.localStorage.setItem(key, userId);
    },
    removeSelectedUserId(key) {
        window.localStorage.removeItem(key);
    }
};
