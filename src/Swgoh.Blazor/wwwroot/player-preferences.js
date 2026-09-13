window.swgohPreferences = {
    getAllyCode: function () {
        return window.localStorage.getItem("swgoh.allyCode");
    },
    setAllyCode: function (allyCode) {
        window.localStorage.setItem("swgoh.allyCode", allyCode);
    },
    clearAllyCode: function () {
        window.localStorage.removeItem("swgoh.allyCode");
    }
};
