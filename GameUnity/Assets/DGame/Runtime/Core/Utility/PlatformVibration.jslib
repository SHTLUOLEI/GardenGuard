mergeInto(LibraryManager.library, {
    DGameVibrate: function (longVibration) {
        if (typeof tt !== 'undefined') {
            if (longVibration && typeof tt.vibrateLong === 'function') tt.vibrateLong({});
            else if (typeof tt.vibrateShort === 'function') tt.vibrateShort({});
        } else if (typeof navigator !== 'undefined' && typeof navigator.vibrate === 'function') {
            navigator.vibrate(longVibration ? 400 : 15);
        }
    }
});
