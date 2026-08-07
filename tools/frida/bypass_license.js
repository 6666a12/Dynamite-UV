// PairIP license check bypass for com.c4cat.dynamix2 (Dynamix Universe)
// Strategy: neuter LicenseClient entry points before the state machine runs,
// block all failure UI / shutdown paths, and auto-finish LicenseActivity.
function whenJavaReady(fn) {
  if (typeof Java !== "undefined" && Java.available) {
    fn();
  } else {
    setTimeout(function () { whenJavaReady(fn); }, 200);
  }
}

whenJavaReady(function () {
Java.perform(function () {
  var LC = Java.use("com.pairip.licensecheck.LicenseClient");

  LC.checkLicense.implementation = function (ctx) {
    console.log("[bypass] LicenseClient.checkLicense() blocked");
  };

  LC.initializeLicenseCheck.implementation = function () {
    console.log("[bypass] initializeLicenseCheck() blocked -> mark FULL_CHECK_OK");
    try {
      var State = Java.use("com.pairip.licensecheck.LicenseClient$LicenseCheckState");
      LC.licenseCheckState.value = State.FULL_CHECK_OK.value;
    } catch (e) {
      console.log("[bypass] set state failed: " + e);
    }
  };

  LC.startErrorDialogActivity.implementation = function () {
    console.log("[bypass] startErrorDialogActivity() blocked");
  };

  LC.startPaywallActivity.implementation = function (pi) {
    console.log("[bypass] startPaywallActivity() blocked");
  };

  LC.scheduleAppShutdown.implementation = function () {
    console.log("[bypass] scheduleAppShutdown() blocked");
  };

  LC.connectToLicensingService.implementation = function (useBackground) {
    console.log("[bypass] connectToLicensingService(" + useBackground + ") blocked");
  };

  try {
    var LA = Java.use("com.pairip.licensecheck.LicenseActivity");
    LA.onCreate.implementation = function (bundle) {
      console.log("[bypass] LicenseActivity.onCreate -> finish()");
      this.finish();
    };
  } catch (e) {
    console.log("[bypass] LicenseActivity hook failed: " + e);
  }

  console.log("[bypass] license hooks installed");
});
});
