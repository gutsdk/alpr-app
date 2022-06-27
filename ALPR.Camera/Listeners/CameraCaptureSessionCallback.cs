using Android.Hardware.Camera2;

namespace ALPRCamera.Droid.Listeners
{
    public class CameraCaptureSessionCallback : CameraCaptureSession.StateCallback
    {
        private readonly Camera2BasicFragment owner;

        public CameraCaptureSessionCallback(Camera2BasicFragment owner)
        {
            if (owner == null)
                throw new System.ArgumentNullException("owner");
            this.owner = owner;
        }

        public override void OnConfigureFailed(CameraCaptureSession session)
        {
            owner.ShowToast("Failed");
        }

        public override void OnConfigured(CameraCaptureSession session)
        {
            if (null == owner.mCameraDevice)
            {
                return;
            }

            owner.mCaptureSession = session;
            try
            {
                owner.mPreviewRequestBuilder.Set(CaptureRequest.ControlAfMode, (int)ControlAFMode.ContinuousPicture);
                owner.SetAutoFlash(owner.mPreviewRequestBuilder);

                owner.mPreviewRequest = owner.mPreviewRequestBuilder.Build();
                owner.mCaptureSession.SetRepeatingRequest(owner.mPreviewRequest,
                        owner.mCaptureCallback, owner.mBackgroundHandler);
            }
            catch (CameraAccessException e)
            {
                e.PrintStackTrace();
            }
        }
    }
}