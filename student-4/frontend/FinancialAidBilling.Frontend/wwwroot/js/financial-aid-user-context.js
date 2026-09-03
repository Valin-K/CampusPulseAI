window.campusPulseFinancialAidUser = {
    getSelectedUserId: function (key) {
        return window.localStorage.getItem(key);
    },
    setSelectedUserId: function (key, value) {
        window.localStorage.setItem(key, value);
    },
    removeSelectedUserId: function (key) {
        window.localStorage.removeItem(key);
    }
};
