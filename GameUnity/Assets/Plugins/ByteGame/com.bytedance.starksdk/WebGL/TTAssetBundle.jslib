mergeInto(LibraryManager.library, {
    StarkAbfsCheckBundleCrcSupport: function() {
        return typeof window !== 'undefined' &&
            !!window.StarkSDK &&
            !!window.StarkSDK.abfs &&
            typeof window.StarkSDK.abfs.calcBundleCrc === 'function';
    },
    StarkAbfsCalcBundleCrc: function(ptr) {
        try {
            // >>> 0 将 CRC 按 uint32 解释，与 C# uint 对齐，避免最高位为 1 时被当作负数。
            return window.StarkSDK.abfs.calcBundleCrc(UTF8ToString(ptr)) >>> 0;
        } catch (e) {
            return 0;
        }
    },
    StarkAbfsCheckReady: function() {
        if (!window.StarkSDK || !window.StarkSDK.abfs)
            return false;
        return window.StarkSDK.abfs.checkReady();
    },
    StarkAbfsRegisterAssetBundleUrl: function(ptr) {
        if (!window.StarkSDK)
            return;

        var url = UTF8ToString(ptr);
        window.StarkSDK.abfs.registerAssetBundleUrl(url);
    },
    StarkAbfsUnregisterAssetBundleUrl: function(ptr) {
        if (!window.StarkSDK)
            return;

        var url = UTF8ToString(ptr);
        window.StarkSDK.abfs.unregisterAssetBundleUrl(url);
    },
    StarkAbfsFetchBundleFromXHR: function(url, id, callback, needRetry) {
        if (!window.StarkSDK)
            return false;

        var _url = UTF8ToString(url);
        var _id = UTF8ToString(id);
        var _callback = function(code, message) {

            var idPtr = 0;
            try {
                var len = lengthBytesUTF8(_id) + 1;
                idPtr = _malloc(len);
                stringToUTF8(_id, idPtr, len);

                dynCall("viii", callback, [idPtr, code, 0]);
            } finally {
                if (idPtr) _free(idPtr);
            }
        }
        window.StarkSDK.abfs.fetchBundleFromXHR(_url, _id, _callback, needRetry);
    }
});
