﻿using System.Collections.Generic;
using Android;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Hardware.Camera2.Params;
using Android.Media;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Java.Nio;
using Size = Android.Util.Size;

namespace Maui.NativeCamera.Views;

// credit: https://github.com/TomerPacific/MediumArticles/blob/master/Camrea2API/app/src/main/java/com/tomerpacific/camera2api/MainActivity.kt
public class NativeCameraPlatformView :
    RelativeLayout,
    TextureView.ISurfaceTextureListener,
    ImageReader.IOnImageAvailableListener
{
    private const int LENS_FACING_FRONT = 0;
    private const int LENS_FACING_BACK = 1;

    private CameraPosition _currentPosition;
    private CameraManager _cameraManager;
    private CameraDevice _cameraDevice;

    private TextureView _textureView;
    private HandlerThread _backgroundHandlerThread;
    private Handler _backgroundHandler;

    private CaptureRequest.Builder _captureRequestBuilder;
    private CameraCaptureSession _cameraCaptureSession;

    private ImageReader _imageReader;
    private Size _previewSize;

    private MediaRecorder _mediaRecorder;
    private bool isRecording = false;
    private Size _videoSize;

    private CameraStateCallback _cameraStateCallback;
    private CaptureStateCallback _captureStateCallback;

    private string cameraId = "";
    private Context _context;

    private bool _havePermissions = false;

    public Action<byte[]> _takePhoto; 

	public NativeCameraPlatformView(
        Context context,
        NativeCameraView nativeCameraView) : base(context)
	{
        _context = context;
        _currentPosition = nativeCameraView.CameraPosition;
        nativeCameraView.SwitchCameraPosition = this.SwitchCamera;
        nativeCameraView.TakePhoto = this.TakePhoto;
        nativeCameraView.GetCameraFeedFrame = this.GetCameraFeedFrame;
        nativeCameraView.StartVideoRecording = this.StartVideoRecording;
        nativeCameraView.EndVideoRecording = this.EndVideoRecording;

        LayoutParameters = new LayoutParams(
            LayoutParams.MatchParent,
            LayoutParams.MatchParent);

        _cameraManager = (CameraManager)Platform.CurrentActivity.GetSystemService(Context.CameraService);

        SetupTextureView();
        StartBackgroundThread();

        CheckPermissions((granted) =>
        {
            if (granted)
            {
                _havePermissions = granted;
                this.OnSurfaceTextureAvailable(
                    _textureView.SurfaceTexture,
                    _textureView.Width,
                    _textureView.Height);
            }
        });
	}

    protected override void Dispose(bool disposing)
    {
        StopBackgroundThread();
        _cameraStateCallback = null;
        _captureStateCallback = null;
        _mediaRecorder = null;
        _imageReader = null;
        _cameraCaptureSession = null;
        _captureRequestBuilder = null;
        _backgroundHandler = null;
        _backgroundHandlerThread = null;
        _textureView = null;
        _cameraDevice = null;
        _cameraManager = null;

        base.Dispose(disposing);
    }

    #region API
    private void SwitchCamera(CameraPosition cameraPosition)
    {
        _cameraDevice.Close();
        _currentPosition = cameraPosition;
        SetupCamera();
        ConnectCamera();
    }

    private void TakePhoto(Action<byte[]> completion)
    {
        // TODO: figure out how to save as a PNG instead
        // and why its not saving to file system correctly
        _takePhoto = completion;
        _captureRequestBuilder = _cameraDevice.CreateCaptureRequest(CameraTemplate.StillCapture);
        _captureRequestBuilder.AddTarget(_imageReader.Surface);
        _cameraCaptureSession.Capture(_captureRequestBuilder.Build(), new CaptureCallback(), null);
    }

    private void GetCameraFeedFrame(Action<byte[]> completion)
    {
        // TODO:
    }

    private void StartVideoRecording()
    {
        // TODO:
    }

    private void EndVideoRecording(Action<byte[]> completion)
    {
        // TODO: 
    }
    #endregion

    #region Setup
    private async void CheckPermissions(Action<bool> completion)
    {
        PermissionStatus cameraStatus = await Permissions.RequestAsync<Permissions.Camera>();

        if (!OperatingSystem.IsAndroidVersionAtLeast(32)) // meaning API < 32
        {
            PermissionStatus readPermission = await Permissions.RequestAsync<Permissions.StorageRead>();
            PermissionStatus writePermission = await Permissions.RequestAsync<Permissions.StorageWrite>();

            if (cameraStatus == PermissionStatus.Granted &&
                readPermission == PermissionStatus.Granted &&
                writePermission == PermissionStatus.Granted)
            {
                completion(true);
            }
            else
            {
                completion(false);
            }
        }
        else
        {
            if (cameraStatus == PermissionStatus.Granted)
            {
                completion(true);
            }
            else
            {
                completion(false);
            }
        }   
    }

    private void SetupCamera()
    {
        var cameraIds = _cameraManager.GetCameraIdList();
        var desiredCameraId = "";

        CameraCharacteristics cameraCharacteristics = null;
        foreach (var id in cameraIds)
        {
            cameraCharacteristics = _cameraManager.GetCameraCharacteristics(id);
            var lensFacing = (int)cameraCharacteristics.Get(CameraCharacteristics.LensFacing);
            bool isFrontFacing = lensFacing == LENS_FACING_FRONT;
            bool isRearFacing = lensFacing == LENS_FACING_BACK;

            if (isFrontFacing && _currentPosition == CameraPosition.FrontFacing)
            {
                desiredCameraId = id;
                break;
            }
            else if (isRearFacing && _currentPosition == CameraPosition.RearFacing)
            {
                desiredCameraId = id;
                break;
            }
        }

        if (string.IsNullOrEmpty(desiredCameraId) || cameraCharacteristics == null)
        {
            return;
        }

        StreamConfigurationMap configurationMap = (StreamConfigurationMap)cameraCharacteristics.Get(CameraCharacteristics.ScalerStreamConfigurationMap);
        if (configurationMap == null)
        {
            return;
        }

        _previewSize = configurationMap.GetOutputSizes((int)ImageFormatType.Jpeg).MaxBy((it) => { return it.Height * it.Width; });
        _videoSize = configurationMap.GetOutputSizes(Java.Lang.Class.FromType(typeof(MediaRecorder))).MaxBy((it) => { return it.Height * it.Width; });
        _imageReader = ImageReader.NewInstance(_previewSize.Width, _previewSize.Height, ImageFormatType.Jpeg, 1);
        _imageReader.SetOnImageAvailableListener(this, _backgroundHandler);

        cameraId = desiredCameraId;
    }

    private void ConnectCamera()
    {
        _cameraManager.OpenCamera(cameraId, _cameraStateCallback, _backgroundHandler);
    }

    private void SetupTextureView()
    {
        _textureView = new TextureView(_context);
        _textureView.LayoutParameters = new LayoutParams(
            LayoutParams.MatchParent,
            LayoutParams.MatchParent);
        _textureView.SurfaceTextureListener = this;
        AddView(_textureView);
    }

    [Obsolete]
    private void SetupCameraCallbacks()
    {
        _captureStateCallback = new CaptureStateCallback(
            configureFailed: (session) => { },
            configured: (session) =>
            {
                _cameraCaptureSession = session;

                _cameraCaptureSession.SetRepeatingRequest(
                    _captureRequestBuilder.Build(),
                    null,
                    _backgroundHandler);
            });

        _cameraStateCallback = new CameraStateCallback(
            disconnected: (camera) => { },
            error: (camera, error) => { },
            opened: (camera) =>
            {
                _cameraDevice = camera;
                var surfaceTexture = _textureView.SurfaceTexture;
                surfaceTexture.SetDefaultBufferSize(_previewSize.Width, _previewSize.Height);
                var previewSurface = new Surface(surfaceTexture);

                _captureRequestBuilder = _cameraDevice.CreateCaptureRequest(CameraTemplate.Preview);
                _captureRequestBuilder.AddTarget(previewSurface);

                _cameraDevice.CreateCaptureSession(
                    new List<Surface>() { previewSurface,  _imageReader.Surface },
                    _captureStateCallback,
                    null);
            });
    }

    private void StartBackgroundThread()
    {
        _backgroundHandlerThread = new HandlerThread("CameraVideoThread");
        _backgroundHandlerThread.Start();
        _backgroundHandler = new Handler(_backgroundHandlerThread.Looper);
    }

    private void StopBackgroundThread()
    {
        _backgroundHandlerThread.QuitSafely();
        _backgroundHandlerThread.Join();
    }
    #endregion

    #region Surface Texture Listener
    public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
    {
        if (surface == null || !_havePermissions)
        {
            return;
        }

        SetupCameraCallbacks();
        SetupCamera();
        ConnectCamera();
    }

    public void OnSurfaceTextureUpdated(SurfaceTexture surface)
    {
        // TODO: save a frame at 24 fps for GetFrameMethod
    }

    public bool OnSurfaceTextureDestroyed(SurfaceTexture surface) => true;
    public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height) { }
    #endregion

    #region IOnImageListener
    public void OnImageAvailable(ImageReader reader)
    {
        var image = reader.AcquireLatestImage();
        ByteBuffer buffer = image.GetPlanes()[0].Buffer;
        byte[] bytes = new byte[buffer.Capacity()];
        buffer.Get(bytes);
        image.Close();

        _takePhoto?.Invoke(bytes);
    }
    #endregion
}

public class CameraStateCallback : CameraDevice.StateCallback
{
    public Action<CameraDevice> Disconnected;
    public Action<CameraDevice, CameraError> Error;
    public Action<CameraDevice> Opened;

    public CameraStateCallback(
        Action<CameraDevice> disconnected,
        Action<CameraDevice> opened,
        Action<CameraDevice, CameraError> error)
    {
        Disconnected = disconnected;
        Opened = opened;
        Error = error;
    }

    public override void OnDisconnected(CameraDevice camera)
    {
        Disconnected?.Invoke(camera);
    }

    public override void OnError(CameraDevice camera, [GeneratedEnum] CameraError error)
    {
        Error?.Invoke(camera, error);
    }

    public override void OnOpened(CameraDevice camera)
    {
        Opened?.Invoke(camera);
    }
}

public class CaptureStateCallback : CameraCaptureSession.StateCallback
{
    public Action<CameraCaptureSession> Configured;
    public Action<CameraCaptureSession> ConfigureFailed;

    public CaptureStateCallback(
        Action<CameraCaptureSession> configured,
        Action<CameraCaptureSession> configureFailed)
    {
        Configured = configured;
        ConfigureFailed = configureFailed;
    }

    public override void OnConfigured(CameraCaptureSession session)
    {
        Configured?.Invoke(session);
    }

    public override void OnConfigureFailed(CameraCaptureSession session)
    {
        ConfigureFailed?.Invoke(session);
    }
}

public class CaptureCallback : CameraCaptureSession.CaptureCallback
{

}