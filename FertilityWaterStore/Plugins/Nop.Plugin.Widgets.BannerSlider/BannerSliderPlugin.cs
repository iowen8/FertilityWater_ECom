using System.Collections.Generic;
using System.IO;
using System.Web.Routing;
using Nop.Core;
using Nop.Core.Plugins;
using Nop.Services.Cms;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Media;

namespace Nop.Plugin.Widgets.BannerSlider
{
    /// <summary>
    /// PLugin
    /// </summary>
    public class BannerSliderPlugin : BasePlugin, IWidgetPlugin
    {
        private readonly IPictureService _pictureService;
        private readonly ISettingService _settingService;
        private readonly IWebHelper _webHelper;

        public BannerSliderPlugin(IPictureService pictureService, 
            ISettingService settingService, IWebHelper webHelper)
        {
            this._pictureService = pictureService;
            this._settingService = settingService;
            this._webHelper = webHelper;
        }

        /// <summary>
        /// Gets widget zones where this widget should be rendered
        /// </summary>
        /// <returns>Widget zones</returns>
        public IList<string> GetWidgetZones()
        {
            return new List<string>() { "my_slider" };
        }

        /// <summary>
        /// Gets a route for provider configuration
        /// </summary>
        /// <param name="actionName">Action name</param>
        /// <param name="controllerName">Controller name</param>
        /// <param name="routeValues">Route values</param>
        public void GetConfigurationRoute(out string actionName, out string controllerName, out RouteValueDictionary routeValues)
        {
            actionName = "Configure";
            controllerName = "WidgetsBannerSlider";
            routeValues = new RouteValueDictionary() { { "Namespaces", "Nop.Plugin.Widgets.BannerSlider.Controllers" }, { "area", null } };
        }

        /// <summary>
        /// Gets a route for displaying widget
        /// </summary>
        /// <param name="widgetZone">Widget zone where it's displayed</param>
        /// <param name="actionName">Action name</param>
        /// <param name="controllerName">Controller name</param>
        /// <param name="routeValues">Route values</param>
        public void GetDisplayWidgetRoute(string widgetZone, out string actionName, out string controllerName, out RouteValueDictionary routeValues)
        {
            actionName = "PublicInfo";
            controllerName = "WidgetsBannerSlider";
            routeValues = new RouteValueDictionary()
            {
                {"Namespaces", "Nop.Plugin.Widgets.BannerSlider.Controllers"},
                {"area", null},
                {"widgetZone", widgetZone}
            };
        }
        
        /// <summary>
        /// Install plugin
        /// </summary>
        public override void Install()
        {
            //pictures
            var sampleImagesPath = _webHelper.MapPath("~/Plugins/Widgets.BannerSlider/Content/BannerSlider/sample-images/");


            //settings
            var settings = new BannerSliderSettings()
            {
                Picture1Id = _pictureService.InsertPicture(File.ReadAllBytes(sampleImagesPath + "banner1.jpg"), "image/pjpeg", "banner_1", true).Id,
                Text1 = "",
                Link1 = _webHelper.GetStoreLocation(false),
                Picture2Id = _pictureService.InsertPicture(File.ReadAllBytes(sampleImagesPath + "banner2.jpg"), "image/pjpeg", "banner_2", true).Id,
                Text2 = "",
                Link2 = _webHelper.GetStoreLocation(false),
                Picture3Id = _pictureService.InsertPicture(File.ReadAllBytes(sampleImagesPath + "banner3.jpg"), "image/pjpeg", "banner_3", true).Id,
                Text3 = "",
                Link3 = _webHelper.GetStoreLocation(false),
                //Picture4Id = _pictureService.InsertPicture(File.ReadAllBytes(sampleImagesPath + "banner4.jpg"), "image/pjpeg", "banner_4", true).Id,
                //Text4 = "",
                //Link4 = _webHelper.GetStoreLocation(false),
            };
            _settingService.SaveSetting(settings);


            this.AddOrUpdatePluginLocaleResource("Plugins.Widgets.BannerSlider.Picture1", "Picture 1");
            this.AddOrUpdatePluginLocaleResource("Plugins.Widgets.BannerSlider.Picture2", "Picture 2");
            this.AddOrUpdatePluginLocaleResource("Plugins.Widgets.BannerSlider.Picture3", "Picture 3");
            this.AddOrUpdatePluginLocaleResource("Plugins.Widgets.BannerSlider.Picture4", "Picture 4");
            this.AddOrUpdatePluginLocaleResource("Plugins.Widgets.BannerSlider.Picture5", "Picture 5");
            this.AddOrUpdatePluginLocaleResource("Plugins.Widgets.BannerSlider.Picture", "Picture");
            this.AddOrUpdatePluginLocaleResource("Plugins.Widgets.BannerSlider.Picture.Hint", "Upload picture.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Widgets.BannerSlider.Text", "Comment");
            this.AddOrUpdatePluginLocaleResource("Plugins.Widgets.BannerSlider.Text.Hint", "Enter comment for picture. Leave empty if you don't want to display any text.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Widgets.BannerSlider.Link", "URL");
            this.AddOrUpdatePluginLocaleResource("Plugins.Widgets.BannerSlider.Link.Hint", "Enter URL. Leave empty if you don't want this picture to be clickable.");

            base.Install();
        }

        /// <summary>
        /// Uninstall plugin
        /// </summary>
        public override void Uninstall()
        {
            //settings
            _settingService.DeleteSetting<BannerSliderSettings>();

            //locales
            this.DeletePluginLocaleResource("Plugins.Widgets.BannerSlider.Picture1");
            this.DeletePluginLocaleResource("Plugins.Widgets.BannerSlider.Picture2");
            this.DeletePluginLocaleResource("Plugins.Widgets.BannerSlider.Picture3");
            this.DeletePluginLocaleResource("Plugins.Widgets.BannerSlider.Picture4");
            this.DeletePluginLocaleResource("Plugins.Widgets.BannerSlider.Picture5");
            this.DeletePluginLocaleResource("Plugins.Widgets.BannerSlider.Picture");
            this.DeletePluginLocaleResource("Plugins.Widgets.BannerSlider.Picture.Hint");
            this.DeletePluginLocaleResource("Plugins.Widgets.BannerSlider.Text");
            this.DeletePluginLocaleResource("Plugins.Widgets.BannerSlider.Text.Hint");
            this.DeletePluginLocaleResource("Plugins.Widgets.BannerSlider.Link");
            this.DeletePluginLocaleResource("Plugins.Widgets.BannerSlider.Link.Hint");
            
            base.Uninstall();
        }
    }
}
