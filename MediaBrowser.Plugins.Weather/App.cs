﻿using MediaBrowser.Plugins.Weather.Pages;
using MediaBrowser.Theater.Interfaces.Navigation;
using MediaBrowser.Theater.Interfaces.Presentation;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MediaBrowser.Plugins.Weather
{
    public class App : IApp
    {
        private readonly INavigationService _nav;
        private readonly IImageManager _imageManager;

        public App(INavigationService nav, IImageManager imageManager)
        {
            _nav = nav;
            _imageManager = imageManager;
        }

        public string Name
        {
            get { return "Weather"; }
        }

        public Page GetLaunchPage()
        {
            return new MainWeatherPage();
        }

        public FrameworkElement GetTileImage()
        {
            var image = new Image
            {
                Source = _imageManager.GetBitmapImage(Plugin.GetThumbUri())
            };

            return image;
        }

        public Task Launch()
        {
            return _nav.Navigate(new MainWeatherPage());
        }

        public void Dispose()
        {
        }
    }

    public class AppFactory : IAppFactory
    {
        public IEnumerable<Type> AppTypes
        {
            get { return new[] { typeof(App) }; }
        }
    }
}
