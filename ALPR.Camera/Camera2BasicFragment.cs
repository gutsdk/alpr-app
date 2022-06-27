using System;
using System.Collections.Generic;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Android.Hardware.Camera2;
using Android.Graphics;
using Android.Hardware.Camera2.Params;
using Android.Media;
using Android.Support.V13.App;
using Android.Support.V4.Content;
using ALPRCamera.Droid.Listeners;
using Java.IO;
using Java.Lang;
using Java.Util;
using Java.Util.Concurrent;
using Boolean = Java.Lang.Boolean;
using Math = Java.Lang.Math;
using Orientation = Android.Content.Res.Orientation;
using System.Threading.Tasks;
using System.Threading;
using OpenALPR_Xamarin.Android_Library;
using Android.Content.Res;
using System.IO;

namespace ALPRCamera.Droid
{
    public class Camera2BasicFragment : Fragment, View.IOnClickListener, FragmentCompat.IOnRequestPermissionsResultCallback
    {
        private static readonly SparseIntArray ORIENTATIONS = new SparseIntArray();
        public static readonly int REQUEST_CAMERA_PERMISSION = 1;
        private static readonly string FRAGMENT_DIALOG = "dialog";

        internal ApplicationInfo applicationInfo;
        internal string OpenALPRConfigFile;
        internal string OpenALPRRuntimeFolder;

        private static readonly string TAG = "Camera2BasicFragment";

        public const int STATE_PREVIEW = 0;

        public const int STATE_WAITING_LOCK = 1;

        public const int STATE_WAITING_PRECAPTURE = 2;

        public const int STATE_WAITING_NON_PRECAPTURE = 3;

        public const int STATE_PICTURE_TAKEN = 4;

        private static readonly int MAX_PREVIEW_WIDTH = 1920;

        private static readonly int MAX_PREVIEW_HEIGHT = 1080;

        private Camera2BasicSurfaceTextureListener mSurfaceTextureListener;

        private string mCameraId;

        private AutoFitTextureView mTextureView;

        private View view;

        public CameraCaptureSession mCaptureSession;

        public CameraDevice mCameraDevice;

        private Size mPreviewSize;

        private CameraStateListener mStateCallback;

        private HandlerThread mBackgroundThread;

        public Handler mBackgroundHandler;

        private ImageReader mImageReader;

        public Java.IO.File mFile;

        private ImageAvailableListener mOnImageAvailableListener;

        public CaptureRequest.Builder mPreviewRequestBuilder;

        public CaptureRequest mPreviewRequest;

        public int mState = STATE_PREVIEW;

        public Java.Util.Concurrent.Semaphore mCameraOpenCloseLock = new Java.Util.Concurrent.Semaphore(1);

        private bool mFlashSupported;

        private int mSensorOrientation;

        public CameraCaptureListener mCaptureCallback;

        const int ACTIVE = 1;
        const int NOTACTIVE = 0;

        internal int sessionIsActive = NOTACTIVE;
        internal Task sessionTask;
        internal CancellationToken cancelToken;

        internal OpenALPR OpenALPRInstance;
        public void ShowToast(string text)
        {
            if (Activity != null)
            {
                Activity.RunOnUiThread(new ShowToastRunnable(Activity.ApplicationContext, text));
            }
        }
        private class ShowToastRunnable : Java.Lang.Object, IRunnable
        {
            private string text;
            private Context context;
            public ShowToastRunnable(Context context, string text)
            {
                this.context = context;
                this.text = text;
            }
            public void Run()
            {
                Toast.MakeText(context, text, ToastLength.Short).Show();
            }
        }
        private static Size ChooseOptimalSize(Size[] choices, int textureViewWidth,
            int textureViewHeight, int maxWidth, int maxHeight, Size aspectRatio)
        {
            var bigEnough = new List<Size>();
            var notBigEnough = new List<Size>();
            int w = aspectRatio.Width;
            int h = aspectRatio.Height;

            for (var i = 0; i < choices.Length; i++)
            {
                Size option = choices[i];
                if ((option.Width <= maxWidth) && (option.Height <= maxHeight) &&
                       option.Height == option.Width * h / w)
                {
                    if (option.Width >= textureViewWidth &&
                        option.Height >= textureViewHeight)
                    {
                        bigEnough.Add(option);
                    }
                    else
                    {
                        notBigEnough.Add(option);
                    }
                }
            }
            if (bigEnough.Count > 0)
            {
                return (Size)Collections.Min(bigEnough, new CompareSizesByArea());
            }
            else if (notBigEnough.Count > 0)
            {
                return (Size)Collections.Max(notBigEnough, new CompareSizesByArea());
            }
            else
            {
                Log.Error(TAG, "Couldn't find any suitable preview size");
                return choices[0];
            }
        }
        public static Camera2BasicFragment NewInstance()
        {
            return new Camera2BasicFragment();
        }
        public override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            AssetManager assets = Activity.Assets;
            var filesToCopy = new string[] { "runtime_data/openalpr.conf"
                                            , "runtime_data/region/eu.xml"
                                           };

            var AndroidDataDir = Android.App.Application.Context.DataDir.AbsolutePath;
            var runtimeFolder = new Java.IO.File(AndroidDataDir, "/files/runtime_data");
            var renamedRuntimeFolder = new Java.IO.File(AndroidDataDir, "runtime_data");
            runtimeFolder.RenameTo(renamedRuntimeFolder);

            var list = renamedRuntimeFolder.List();

            OpenALPRConfigFile = AndroidDataDir + "/runtime_data/openalpr.conf";

            var f = new Java.IO.File(AndroidDataDir);
            var flist = f.List();
            var lib = new Java.IO.File(f, "lib");
            var liblist = lib.List();

            mStateCallback = new CameraStateListener(this);
            mSurfaceTextureListener = new Camera2BasicSurfaceTextureListener(this);
            OpenALPRInstance = new OpenALPR(this.Activity, AndroidDataDir, OpenALPRConfigFile, "eu", "ru");

            ORIENTATIONS.Append((int)SurfaceOrientation.Rotation0, 90);
            ORIENTATIONS.Append((int)SurfaceOrientation.Rotation90, 0);
            ORIENTATIONS.Append((int)SurfaceOrientation.Rotation180, 270);
            ORIENTATIONS.Append((int)SurfaceOrientation.Rotation270, 180);
        }
        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            return inflater.Inflate(Resource.Layout.fragment_camera2_basic, container, false);
        }
        public override void OnViewCreated(View view, Bundle savedInstanceState)
        {
            mTextureView = (AutoFitTextureView)view.FindViewById(Resource.Id.texture);
            view.FindViewById(Resource.Id.picture).SetOnClickListener(this);
            view.FindViewById(Resource.Id.info).SetOnClickListener(this);
            this.view = view;
        }
        public override void OnActivityCreated(Bundle savedInstanceState)
        {
            base.OnActivityCreated(savedInstanceState);
            mFile = new Java.IO.File(Activity.GetExternalFilesDir(null), "pic.jpg");
            mCaptureCallback = new CameraCaptureListener(this);
            mOnImageAvailableListener = new ImageAvailableListener(this, mFile);
        }
        public override void OnResume()
        {
            base.OnResume();
            StartBackgroundThread();
            if (mTextureView.IsAvailable)
            {
                OpenCamera(mTextureView.Width, mTextureView.Height);
            }
            else
            {
                mTextureView.SurfaceTextureListener = mSurfaceTextureListener;
            }
        }
        public override void OnPause()
        {
            StopCaptureSession();
            (this.view.FindViewById(Resource.Id.picture) as Button).Text = "Начать";
            CloseCamera();
            StopBackgroundThread();
            base.OnPause();
        }
        private void RequestCameraPermission()
        {
            if (FragmentCompat.ShouldShowRequestPermissionRationale(this, Manifest.Permission.Camera))
            {
                new ConfirmationDialog().Show(ChildFragmentManager, FRAGMENT_DIALOG);
            }
            else
            {
                FragmentCompat.RequestPermissions(this, new string[] { Manifest.Permission.Camera },
                        REQUEST_CAMERA_PERMISSION);
            }
        }
        public void OnRequestPermissionsResult(int requestCode, string[] permissions, int[] grantResults)
        {
            if (requestCode != REQUEST_CAMERA_PERMISSION)
                return;

            if (grantResults.Length != 1 || grantResults[0] != (int)Permission.Granted)
            {
                ErrorDialog.NewInstance(GetString(Resource.String.request_permission))
                        .Show(ChildFragmentManager, FRAGMENT_DIALOG);
            }
        }
        private void SetUpCameraOutputs(int width, int height)
        {
            var activity = Activity;
            var manager = (CameraManager)activity.GetSystemService(Context.CameraService);
            try
            {
                for (var i = 0; i < manager.GetCameraIdList().Length; i++)
                {
                    var cameraId = manager.GetCameraIdList()[i];
                    CameraCharacteristics characteristics = manager.GetCameraCharacteristics(cameraId);

                    var facing = (Integer)characteristics.Get(CameraCharacteristics.LensFacing);
                    if (facing != null && facing == (Integer.ValueOf((int)LensFacing.Front)))
                    {
                        continue;
                    }

                    var map = (StreamConfigurationMap)characteristics.Get(CameraCharacteristics.ScalerStreamConfigurationMap);
                    if (map == null)
                    {
                        continue;
                    }

                    Size largest = (Size)Collections.Max(Arrays.AsList(map.GetOutputSizes((int)ImageFormatType.Jpeg)),
                        new CompareSizesByArea());
                    mImageReader = ImageReader.NewInstance(largest.Width, largest.Height, ImageFormatType.Jpeg, /*maxImages*/2);
                    mImageReader.SetOnImageAvailableListener(mOnImageAvailableListener, mBackgroundHandler);

                    var displayRotation = activity.WindowManager.DefaultDisplay.Rotation;
                    mSensorOrientation = (int)characteristics.Get(CameraCharacteristics.SensorOrientation);
                    bool swappedDimensions = false;
                    switch (displayRotation)
                    {
                        case SurfaceOrientation.Rotation0:
                        case SurfaceOrientation.Rotation180:
                            if (mSensorOrientation == 90 || mSensorOrientation == 270)
                            {
                                swappedDimensions = true;
                            }
                            break;
                        case SurfaceOrientation.Rotation90:
                        case SurfaceOrientation.Rotation270:
                            if (mSensorOrientation == 0 || mSensorOrientation == 180)
                            {
                                swappedDimensions = true;
                            }
                            break;
                        default:
                            Log.Error(TAG, "Display rotation is invalid: " + displayRotation);
                            break;
                    }

                    Point displaySize = new Point();
                    activity.WindowManager.DefaultDisplay.GetSize(displaySize);
                    var rotatedPreviewWidth = width;
                    var rotatedPreviewHeight = height;
                    var maxPreviewWidth = displaySize.X;
                    var maxPreviewHeight = displaySize.Y;

                    if (swappedDimensions)
                    {
                        rotatedPreviewWidth = height;
                        rotatedPreviewHeight = width;
                        maxPreviewWidth = displaySize.Y;
                        maxPreviewHeight = displaySize.X;
                    }

                    if (maxPreviewWidth > MAX_PREVIEW_WIDTH)
                    {
                        maxPreviewWidth = MAX_PREVIEW_WIDTH;
                    }

                    if (maxPreviewHeight > MAX_PREVIEW_HEIGHT)
                    {
                        maxPreviewHeight = MAX_PREVIEW_HEIGHT;
                    }

                    mPreviewSize = ChooseOptimalSize(map.GetOutputSizes(Class.FromType(typeof(SurfaceTexture))),
                        rotatedPreviewWidth, rotatedPreviewHeight, maxPreviewWidth,
                        maxPreviewHeight, largest);

                    var orientation = Resources.Configuration.Orientation;
                    if (orientation == Orientation.Landscape)
                    {
                        mTextureView.SetAspectRatio(mPreviewSize.Width, mPreviewSize.Height);
                    }
                    else
                    {
                        mTextureView.SetAspectRatio(mPreviewSize.Height, mPreviewSize.Width);
                    }

                    var available = (Boolean)characteristics.Get(CameraCharacteristics.FlashInfoAvailable);
                    if (available == null)
                    {
                        mFlashSupported = false;
                    }
                    else
                    {
                        mFlashSupported = (bool)available;
                    }

                    mCameraId = cameraId;
                    return;
                }
            }
            catch (CameraAccessException e)
            {
                e.PrintStackTrace();
            }
            catch (NullPointerException e)
            {
                ErrorDialog.NewInstance(GetString(Resource.String.camera_error)).Show(ChildFragmentManager, FRAGMENT_DIALOG);
            }
        }
        public void OpenCamera(int width, int height)
        {
            if (ContextCompat.CheckSelfPermission(Activity, Manifest.Permission.Camera) != Permission.Granted)
            {
                RequestCameraPermission();
                return;
            }
            SetUpCameraOutputs(width, height);
            ConfigureTransform(width, height);
            var activity = Activity;
            var manager = (CameraManager)activity.GetSystemService(Context.CameraService);
            try
            {
                if (!mCameraOpenCloseLock.TryAcquire(2500, TimeUnit.Milliseconds))
                {
                    throw new RuntimeException("Time out waiting to lock camera opening.");
                }
                manager.OpenCamera(mCameraId, mStateCallback, mBackgroundHandler);
            }
            catch (CameraAccessException e)
            {
                e.PrintStackTrace();
            }
            catch (InterruptedException e)
            {
                throw new RuntimeException("Interrupted while trying to lock camera opening.", e);
            }
        }
        private void CloseCamera()
        {
            try
            {
                mCameraOpenCloseLock.Acquire();
                if (null != mCaptureSession)
                {
                    mCaptureSession.Close();
                    mCaptureSession = null;
                }
                if (null != mCameraDevice)
                {
                    mCameraDevice.Close();
                    mCameraDevice = null;
                }
                if (null != mImageReader)
                {
                    mImageReader.Close();
                    mImageReader = null;
                }
            }
            catch (InterruptedException e)
            {
                throw new RuntimeException("Interrupted while trying to lock camera closing.", e);
            }
            finally
            {
                mCameraOpenCloseLock.Release();
            }
        }
        public void SetAutoFlash(CaptureRequest.Builder requestBuilder)
        {
            if (mFlashSupported)
            {
                requestBuilder.Set(CaptureRequest.ControlAeMode, (int)ControlAEMode.OnAutoFlash);
            }
        }
        private void StartBackgroundThread()
        {
            mBackgroundThread = new HandlerThread("CameraBackground");
            mBackgroundThread.Start();
            mBackgroundHandler = new Handler(mBackgroundThread.Looper);
        }
        private void StopBackgroundThread()
        {
            mBackgroundThread.QuitSafely();
            try
            {
                mBackgroundThread.Join();
                mBackgroundThread = null;
                mBackgroundHandler = null;
            }
            catch (InterruptedException e)
            {
                e.PrintStackTrace();
            }
        }
        public void CreateCameraPreviewSession()
        {
            try
            {
                SurfaceTexture texture = mTextureView.SurfaceTexture;
                if (texture == null)
                {
                    throw new IllegalStateException("texture is null");
                }

                texture.SetDefaultBufferSize(mPreviewSize.Width, mPreviewSize.Height);

                Surface surface = new Surface(texture);

                mPreviewRequestBuilder = mCameraDevice.CreateCaptureRequest(CameraTemplate.Preview);
                mPreviewRequestBuilder.AddTarget(surface);

                List<Surface> surfaces = new List<Surface>();
                surfaces.Add(surface);
                surfaces.Add(mImageReader.Surface);
                mCameraDevice.CreateCaptureSession(surfaces, new CameraCaptureSessionCallback(this), null);

            }
            catch (CameraAccessException e)
            {
                e.PrintStackTrace();
            }
        }
        public static T Cast<T>(Java.Lang.Object obj) where T : class
        {
            var propertyInfo = obj.GetType().GetProperty("Instance");
            return propertyInfo == null ? null : propertyInfo.GetValue(obj, null) as T;
        }
        public void ConfigureTransform(int viewWidth, int viewHeight)
        {
            Activity activity = Activity;
            if (null == mTextureView || null == mPreviewSize || null == activity)
            {
                return;
            }
            var rotation = (int)activity.WindowManager.DefaultDisplay.Rotation;
            Matrix matrix = new Matrix();
            RectF viewRect = new RectF(0, 0, viewWidth, viewHeight);
            RectF bufferRect = new RectF(0, 0, mPreviewSize.Height, mPreviewSize.Width);
            float centerX = viewRect.CenterX();
            float centerY = viewRect.CenterY();
            if ((int)SurfaceOrientation.Rotation90 == rotation || (int)SurfaceOrientation.Rotation270 == rotation)
            {
                bufferRect.Offset(centerX - bufferRect.CenterX(), centerY - bufferRect.CenterY());
                matrix.SetRectToRect(viewRect, bufferRect, Matrix.ScaleToFit.Fill);
                float scale = Math.Max((float)viewHeight / mPreviewSize.Height, (float)viewWidth / mPreviewSize.Width);
                matrix.PostScale(scale, scale, centerX, centerY);
                matrix.PostRotate(90 * (rotation - 2), centerX, centerY);
            }
            else if ((int)SurfaceOrientation.Rotation180 == rotation)
            {
                matrix.PostRotate(180, centerX, centerY);
            }
            mTextureView.SetTransform(matrix);
        }
        private void TakePicture()
        {
            LockFocus();
        }
        private void LockFocus()
        {
            try
            {
                mPreviewRequestBuilder.Set(CaptureRequest.ControlAfTrigger, (int)ControlAFTrigger.Start);
                mState = STATE_WAITING_LOCK;
                mCaptureSession.Capture(mPreviewRequestBuilder.Build(), mCaptureCallback,
                        mBackgroundHandler);
            }
            catch (CameraAccessException e)
            {
                e.PrintStackTrace();
            }
        }
        public void RunPrecaptureSequence()
        {
            try
            {
                mPreviewRequestBuilder.Set(CaptureRequest.ControlAePrecaptureTrigger, (int)ControlAEPrecaptureTrigger.Start);
                mState = STATE_WAITING_PRECAPTURE;
                mCaptureSession.Capture(mPreviewRequestBuilder.Build(), mCaptureCallback, mBackgroundHandler);
            }
            catch (CameraAccessException e)
            {
                e.PrintStackTrace();
            }
        }
        private CaptureRequest.Builder stillCaptureBuilder;
        public void CaptureStillPicture()
        {
            try
            {
                var activity = Activity;
                if (null == activity || null == mCameraDevice)
                {
                    return;
                }
                if (stillCaptureBuilder == null)
                    stillCaptureBuilder = mCameraDevice.CreateCaptureRequest(CameraTemplate.StillCapture);

                stillCaptureBuilder.AddTarget(mImageReader.Surface);

                stillCaptureBuilder.Set(CaptureRequest.ControlAfMode, (int)ControlAFMode.ContinuousPicture);
                SetAutoFlash(stillCaptureBuilder);

                int rotation = (int)activity.WindowManager.DefaultDisplay.Rotation;
                stillCaptureBuilder.Set(CaptureRequest.JpegOrientation, GetOrientation(rotation));

                mCaptureSession.StopRepeating();
                mCaptureSession.Capture(stillCaptureBuilder.Build(), new CameraCaptureStillPictureSessionCallback(this), null);
            }
            catch (CameraAccessException e)
            {
                e.PrintStackTrace();
            }
        }
        private int GetOrientation(int rotation)
        {
            return (ORIENTATIONS.Get(rotation) + mSensorOrientation + 270) % 360;
        }
        public void UnlockFocus()
        {
            try
            {
                mPreviewRequestBuilder.Set(CaptureRequest.ControlAfTrigger, (int)ControlAFTrigger.Cancel);
                SetAutoFlash(mPreviewRequestBuilder);
                mCaptureSession.Capture(mPreviewRequestBuilder.Build(), mCaptureCallback,
                        mBackgroundHandler);
                mState = STATE_PREVIEW;
                mCaptureSession.SetRepeatingRequest(mPreviewRequest, mCaptureCallback,
                        mBackgroundHandler);
            }
            catch (CameraAccessException e)
            {
                e.PrintStackTrace();
            }
        }
        public void OnClick(View v)
        {
            if (v.Id == Resource.Id.picture)
            {
                if (Interlocked.CompareExchange(ref sessionIsActive, ACTIVE, NOTACTIVE) == NOTACTIVE)
                {
                    var button = v as Button;
                    button.Text = "Остановить";
                    StartCaptureSession();
                }
                else
                {
                    var button = v as Button;
                    button.Text = "Начать";
                    StopCaptureSession();
                }
            }
            else if (v.Id == Resource.Id.info)
            {

                EventHandler<DialogClickEventArgs> nullHandler = null;
                Activity activity = Activity;
                if (activity != null)
                {
                    new AlertDialog.Builder(activity)
                        .SetMessage("OpenALPR и Camera2Api")
                        .SetPositiveButton(Android.Resource.String.Ok, nullHandler)
                        .Show();
                }
            }
        }
        private void StartCaptureSession()
        {
            sessionTask = Task.Factory.StartNew(async () =>
            {
                while (Interlocked.CompareExchange(ref sessionIsActive, ACTIVE, ACTIVE) == ACTIVE)
                {
                    var imageBytes = await TakePhoto();
                    var file = SavePhoto(imageBytes);

                    var results = OpenALPRInstance.Recognize(file.AbsolutePath, 10);
                    string output = "";

                    if (results.DidErrorOccur == false)
                    {
                        foreach (var rr in results.FoundLicensePlates)
                        {
                            output += "Лучшее: " + rr.BestLicensePlate + "(" + rr.Confidence + "%)\n";

                            ShowToast(output);
                        }
                    }

                    var delete = true;
                    if (delete)
                    {
                        System.IO.File.Delete(file.AbsolutePath);
                    }
                }
            });
        }
        private void StopCaptureSession()
        {
            Interlocked.Exchange(ref sessionIsActive, NOTACTIVE);
        }
        public async Task<byte[]> TakePhoto()
        {
            try
            {
                var ratio = ((decimal)mPreviewSize.Height) / mPreviewSize.Width;
                var image = Bitmap.CreateBitmap(mTextureView.Bitmap, 0, 0, mTextureView.Bitmap.Width, (int)(mTextureView.Bitmap.Width * ratio));
                byte[] imageBytes = null;

                using (var imageStream = new System.IO.MemoryStream())
                {

                    var success = image.Compress(Bitmap.CompressFormat.Jpeg, 100, imageStream);
                    image.Recycle();
                    imageBytes = imageStream.ToArray();

                    return imageBytes;
                }
            }
            catch (System.Exception exc)
            {
                return null;
            }
        }
        private Java.IO.File SavePhoto(byte[] imageBytes)
        {
            var file = new Java.IO.File(Activity.GetExternalFilesDir(null),
                DateTime.Now.ToString("h:mm:ss") + Guid.NewGuid().ToString() + ".jpg");

            using (var output = new FileOutputStream(file))
            {
                try
                {
                    output.Write(imageBytes);
                    output.Close();
                }
                catch (Java.IO.IOException e)
                {
                    e.PrintStackTrace();
                    return null;
                }
                catch (System.Exception e)
                {
                    return null;
                }
            }

            return file;
        }
    }
}