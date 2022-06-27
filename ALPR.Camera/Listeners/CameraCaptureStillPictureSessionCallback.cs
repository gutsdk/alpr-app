using Android.Hardware.Camera2;
using Android.Util;

namespace ALPRCamera.Droid.Listeners
{
    public class CameraCaptureStillPictureSessionCallback : CameraCaptureSession.CaptureCallback
    {
        private static readonly string TAG = "CameraCaptureStillPictureSessionCallback";

        private readonly Camera2BasicFragment owner;

        public CameraCaptureStillPictureSessionCallback(Camera2BasicFragment owner)
        {
            if (owner == null)
                throw new System.ArgumentNullException("owner");
            this.owner = owner;
        }

        public override void OnCaptureCompleted(CameraCaptureSession session, CaptureRequest request, TotalCaptureResult result)
        {
            owner.ShowToast("Saved: " + owner.mFile);
            Log.Debug(TAG, owner.mFile.ToString());
            owner.UnlockFocus();
        }
    }
}
