﻿using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Maui.Storage;
using Maui.NativeCamera;
using static CommunityToolkit.Maui.Markup.GridRowsColumns;

namespace NativeCameraTester;

public class MainPage : ContentPage
{
    private NativeCameraView _cameraView = new()
    {
        CameraPosition = CameraPosition.FrontFacing
    };

    private Button _switchCameraPosition = new()
    {
        Text = "Rear"
    };

    private Button _takePhoto = new()
    {
        Text = "Photo"
    };

    private Button _takeVideo = new()
    {
        Text = "Record"
    };

    public MainPage()
    {
        _switchCameraPosition.Clicked += _switchCameraPosition_Clicked;
        _takePhoto.Clicked += _takePhoto_Clicked;
        _takeVideo.Clicked += _takeVideo_Clicked;

        Content = new Grid
        {
            RowDefinitions = Rows.Define(Star, 80),
            ColumnDefinitions = Columns.Define(Star, Star, Star),
            RowSpacing = 8,
            ColumnSpacing = 8,
            Children =
            {
                _cameraView.Row(0).ColumnSpan(3),
                _switchCameraPosition.Row(1).Column(0),
                _takePhoto.Row(1).Column(1),
                _takeVideo.Row(1).Column(2)
            }
        };
    }

    private void _takeVideo_Clicked(object sender, EventArgs e)
    {
        if (_takeVideo.Text == "Record")
        {
            _cameraView.StartVideoRecording();
            _takeVideo.Text = "Stop Record";
        }
        else if (_takeVideo.Text == "Stop Record")
        {
            _takeVideo.Text = "Record";
            _cameraView.EndVideoRecording((videoData) =>
            {
                using (var stream = new MemoryStream(videoData))
                {
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        await FileSaver.Default.SaveAsync("temp.mov", stream, CancellationToken.None);
                    });
                }
            });
        }

    }

    private void _takePhoto_Clicked(object sender, EventArgs e)
    {
        _cameraView.TakePhoto((photoBytes) =>
        {
            using (var stream = new MemoryStream(photoBytes))
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await FileSaver.Default.SaveAsync("temp.jpeg", stream, CancellationToken.None);
                });
            }
        });
    }

    private void _switchCameraPosition_Clicked(object sender, EventArgs e)
    {
        if (_switchCameraPosition.Text == "Rear")
        {
            _cameraView.SwitchCameraPosition(CameraPosition.RearFacing);
            _switchCameraPosition.Text = "Front";
        }
        else if (_switchCameraPosition.Text == "Front")
        {
            _cameraView.SwitchCameraPosition(CameraPosition.FrontFacing);
            _switchCameraPosition.Text = "Rear";
        }
    }
}
