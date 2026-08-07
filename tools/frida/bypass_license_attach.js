// Attach-mode license bypass: the check already ran and failed.
// Block every failure path, force state, and finish the LicenseActivity.
Java.perform(function () {
  var LC = Java.use("com.pairip.licensecheck.LicenseClient");

  LC.initializeLicenseCheck.implementation = function () {
    console.log("[bypass] initializeLicenseCheck() blocked");
  };
  LC.connectToLicensingService.implementation = function (b) {
    console.log("[bypass] connectToLicensingService() blocked");
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
  LC.handleError.implementation = function (e) {
    console.log("[bypass] handleError() blocked: " + e);
  };
  LC.retryOrThrow.overload("com.pairip.licensecheck.LicenseCheckException").implementation = function (e) {
    console.log("[bypass] retryOrThrow() blocked: " + e);
  };
  LC.retryOrThrow.overload("com.pairip.licensecheck.LicenseCheckException", "boolean", "boolean").implementation = function (e, a, b) {
    console.log("[bypass] retryOrThrow(3) blocked: " + e);
  };

  try {
    var State = Java.use("com.pairip.licensecheck.LicenseClient$LicenseCheckState");
    LC.licenseCheckState.value = State.FULL_CHECK_OK.value;
    console.log("[bypass] state forced to FULL_CHECK_OK");
  } catch (e) {
    console.log("[bypass] set state failed: " + e);
  }

  var LA = Java.use("com.pairip.licensecheck.LicenseActivity");
  LA.onCreate.implementation = function (bundle) {
    console.log("[bypass] LicenseActivity.onCreate -> finish()");
    this.finish();
  };

  // Finish any already-showing LicenseActivity
  Java.choose("com.pairip.licensecheck.LicenseActivity", {
    onMatch: function (inst) {
      console.log("[bypass] finishing existing LicenseActivity " + inst);
      inst.finish();
    },
    onComplete: function () {
      console.log("[bypass] LicenseActivity sweep done");
    }
  });

  console.log("[bypass] attach-mode hooks installed");
});
