﻿using IOSAndroidCameraFeed.Pages;

namespace IOSAndroidCameraFeed;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(nameof(MainPage), typeof(MainPage));
	}
}
