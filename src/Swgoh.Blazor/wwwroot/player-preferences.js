window.swgohPreferences = {
    getAllyCode: function () {
        return window.localStorage.getItem("swgoh.allyCode");
    },
    setAllyCode: function (allyCode) {
        window.localStorage.setItem("swgoh.allyCode", allyCode);
    },
    clearAllyCode: function () {
        window.localStorage.removeItem("swgoh.allyCode");
    },
    getDailyCrystalBudget: function (allyCode) {
        return window.localStorage.getItem("swgoh.dailyCrystalBudget." + allyCode);
    },
    setDailyCrystalBudget: function (allyCode, budget) {
        window.localStorage.setItem("swgoh.dailyCrystalBudget." + allyCode, budget);
    },
    getDailyResourceCadences: function (allyCode) {
        return window.localStorage.getItem("swgoh.dailyResourceCadences." + allyCode);
    },
    setDailyResourceCadences: function (allyCode, cadencesJson) {
        window.localStorage.setItem("swgoh.dailyResourceCadences." + allyCode, cadencesJson);
    }
};
