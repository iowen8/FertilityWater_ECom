using System.Web.Mvc;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Plugin.Widgets.BannerSlider.Infrastructure.Cache;
using Nop.Plugin.Widgets.BannerSlider.Models;
using Nop.Services.Configuration;
using Nop.Services.Media;
using Nop.Services.Stores;
using Nop.Web.Framework.Controllers;

namespace Nop.Plugin.Widgets.BannerSlider.Controllers
{
    public class WidgetsBannerSliderController : BasePluginController
    {
        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly IStoreService _storeService;
        private readonly IPictureService _pictureService;
        private readonly ISettingService _settingService;
        private readonly ICacheManager _cacheManager;

        public WidgetsBannerSliderController(IWorkContext workContext,
            IStoreContext storeContext,
            IStoreService storeService, 
            IPictureService pictureService,
            ISettingService settingService,
            ICacheManager cacheManager)
        {
            this._workContext = workContext;
            this._storeContext = storeContext;
            this._storeService = storeService;
            this._pictureService = pictureService;
            this._settingService = settingService;
            this._cacheManager = cacheManager;
        }

        protected string GetPictureUrl(int pictureId)
        {
            string cacheKey = string.Format(ModelCacheEventConsumer.PICTURE_URL_MODEL_KEY, pictureId);
            return _cacheManager.Get(cacheKey, () =>
            {
                var url = _pictureService.GetPictureUrl(pictureId, showDefaultPicture: false);
                //little hack here. nulls aren't cacheable so set it to ""
                if (url == null)
                    url = "";

                return url;
            });
        }

        [AdminAuthorize]
        [ChildActionOnly]
        public ActionResult Configure()
        {
            //load settings for a chosen store scope
            var storeScope = this.GetActiveStoreScopeConfiguration(_storeService, _workContext);
            var BannerSliderSettings = _settingService.LoadSetting<BannerSliderSettings>(storeScope);
            var model = new ConfigurationModel();
            model.Picture1Id = BannerSliderSettings.Picture1Id;
            model.Text1 = BannerSliderSettings.Text1;
            model.Link1 = BannerSliderSettings.Link1;
            model.Picture2Id = BannerSliderSettings.Picture2Id;
            model.Text2 = BannerSliderSettings.Text2;
            model.Link2 = BannerSliderSettings.Link2;
            model.Picture3Id = BannerSliderSettings.Picture3Id;
            model.Text3 = BannerSliderSettings.Text3;
            model.Link3 = BannerSliderSettings.Link3;
            model.Picture4Id = BannerSliderSettings.Picture4Id;
            model.Text4 = BannerSliderSettings.Text4;
            model.Link4 = BannerSliderSettings.Link4;
            model.Picture5Id = BannerSliderSettings.Picture5Id;
            model.Text5 = BannerSliderSettings.Text5;
            model.Link5 = BannerSliderSettings.Link5;
            model.ActiveStoreScopeConfiguration = storeScope;
            if (storeScope > 0)
            {
                model.Picture1Id_OverrideForStore = _settingService.SettingExists(BannerSliderSettings, x => x.Picture1Id, storeScope);
                model.Text1_OverrideForStore = _settingService.SettingExists(BannerSliderSettings, x => x.Text1, storeScope);
                model.Link1_OverrideForStore = _settingService.SettingExists(BannerSliderSettings, x => x.Link1, storeScope);
                model.Picture2Id_OverrideForStore = _settingService.SettingExists(BannerSliderSettings, x => x.Picture2Id, storeScope);
                model.Text2_OverrideForStore = _settingService.SettingExists(BannerSliderSettings, x => x.Text2, storeScope);
                model.Link2_OverrideForStore = _settingService.SettingExists(BannerSliderSettings, x => x.Link2, storeScope);
                model.Picture3Id_OverrideForStore = _settingService.SettingExists(BannerSliderSettings, x => x.Picture3Id, storeScope);
                model.Text3_OverrideForStore = _settingService.SettingExists(BannerSliderSettings, x => x.Text3, storeScope);
                model.Link3_OverrideForStore = _settingService.SettingExists(BannerSliderSettings, x => x.Link3, storeScope);
                model.Picture4Id_OverrideForStore = _settingService.SettingExists(BannerSliderSettings, x => x.Picture4Id, storeScope);
                model.Text4_OverrideForStore = _settingService.SettingExists(BannerSliderSettings, x => x.Text4, storeScope);
                model.Link4_OverrideForStore = _settingService.SettingExists(BannerSliderSettings, x => x.Link4, storeScope);
                model.Picture5Id_OverrideForStore = _settingService.SettingExists(BannerSliderSettings, x => x.Picture5Id, storeScope);
                model.Text5_OverrideForStore = _settingService.SettingExists(BannerSliderSettings, x => x.Text5, storeScope);
                model.Link5_OverrideForStore = _settingService.SettingExists(BannerSliderSettings, x => x.Link5, storeScope);
            }

            return View("~/Plugins/Widgets.BannerSlider/Views/WidgetsBannerSlider/Configure.cshtml", model);
        }

        [HttpPost]
        [AdminAuthorize]
        [ChildActionOnly]
        public ActionResult Configure(ConfigurationModel model)
        {
            //load settings for a chosen store scope
            var storeScope = this.GetActiveStoreScopeConfiguration(_storeService, _workContext);
            var BannerSliderSettings = _settingService.LoadSetting<BannerSliderSettings>(storeScope);
            BannerSliderSettings.Picture1Id = model.Picture1Id;
            BannerSliderSettings.Text1 = model.Text1;
            BannerSliderSettings.Link1 = model.Link1;
            BannerSliderSettings.Picture2Id = model.Picture2Id;
            BannerSliderSettings.Text2 = model.Text2;
            BannerSliderSettings.Link2 = model.Link2;
            BannerSliderSettings.Picture3Id = model.Picture3Id;
            BannerSliderSettings.Text3 = model.Text3;
            BannerSliderSettings.Link3 = model.Link3;
            BannerSliderSettings.Picture4Id = model.Picture4Id;
            BannerSliderSettings.Text4 = model.Text4;
            BannerSliderSettings.Link4 = model.Link4;
            BannerSliderSettings.Picture5Id = model.Picture5Id;
            BannerSliderSettings.Text5 = model.Text5;
            BannerSliderSettings.Link5 = model.Link5;

            /* We do not clear cache after each setting update.
             * This behavior can increase performance because cached settings will not be cleared 
             * and loaded from database after each update */
            if (model.Picture1Id_OverrideForStore || storeScope == 0)
                _settingService.SaveSetting(BannerSliderSettings, x => x.Picture1Id, storeScope, false);
            else if (storeScope > 0)
                _settingService.DeleteSetting(BannerSliderSettings, x => x.Picture1Id, storeScope);
            
            if (model.Text1_OverrideForStore || storeScope == 0)
                _settingService.SaveSetting(BannerSliderSettings, x => x.Text1, storeScope, false);
            else if (storeScope > 0)
                _settingService.DeleteSetting(BannerSliderSettings, x => x.Text1, storeScope);
            
            if (model.Link1_OverrideForStore || storeScope == 0)
                _settingService.SaveSetting(BannerSliderSettings, x => x.Link1, storeScope, false);
            else if (storeScope > 0)
                _settingService.DeleteSetting(BannerSliderSettings, x => x.Link1, storeScope);
            
            if (model.Picture2Id_OverrideForStore || storeScope == 0)
                _settingService.SaveSetting(BannerSliderSettings, x => x.Picture2Id, storeScope, false);
            else if (storeScope > 0)
                _settingService.DeleteSetting(BannerSliderSettings, x => x.Picture2Id, storeScope);
            
            if (model.Text2_OverrideForStore || storeScope == 0)
                _settingService.SaveSetting(BannerSliderSettings, x => x.Text2, storeScope, false);
            else if (storeScope > 0)
                _settingService.DeleteSetting(BannerSliderSettings, x => x.Text2, storeScope);
            
            if (model.Link2_OverrideForStore || storeScope == 0)
                _settingService.SaveSetting(BannerSliderSettings, x => x.Link2, storeScope, false);
            else if (storeScope > 0)
                _settingService.DeleteSetting(BannerSliderSettings, x => x.Link2, storeScope);
            
            if (model.Picture3Id_OverrideForStore || storeScope == 0)
                _settingService.SaveSetting(BannerSliderSettings, x => x.Picture3Id, storeScope, false);
            else if (storeScope > 0)
                _settingService.DeleteSetting(BannerSliderSettings, x => x.Picture3Id, storeScope);
            
            if (model.Text3_OverrideForStore || storeScope == 0)
                _settingService.SaveSetting(BannerSliderSettings, x => x.Text3, storeScope, false);
            else if (storeScope > 0)
                _settingService.DeleteSetting(BannerSliderSettings, x => x.Text3, storeScope);
            
            if (model.Link3_OverrideForStore || storeScope == 0)
                _settingService.SaveSetting(BannerSliderSettings, x => x.Link3, storeScope, false);
            else if (storeScope > 0)
                _settingService.DeleteSetting(BannerSliderSettings, x => x.Link3, storeScope);
            
            if (model.Picture4Id_OverrideForStore || storeScope == 0)
                _settingService.SaveSetting(BannerSliderSettings, x => x.Picture4Id, storeScope, false);
            else if (storeScope > 0)
                _settingService.DeleteSetting(BannerSliderSettings, x => x.Picture4Id, storeScope);
            
            if (model.Text4_OverrideForStore || storeScope == 0)
                _settingService.SaveSetting(BannerSliderSettings, x => x.Text4, storeScope, false);
            else if (storeScope > 0)
                _settingService.DeleteSetting(BannerSliderSettings, x => x.Text4, storeScope);

            if (model.Link4_OverrideForStore || storeScope == 0)
                _settingService.SaveSetting(BannerSliderSettings, x => x.Link4, storeScope, false);
            else if (storeScope > 0)
                _settingService.DeleteSetting(BannerSliderSettings, x => x.Link4, storeScope);

            if (model.Picture5Id_OverrideForStore || storeScope == 0)
                _settingService.SaveSetting(BannerSliderSettings, x => x.Picture5Id, storeScope, false);
            else if (storeScope > 0)
                _settingService.DeleteSetting(BannerSliderSettings, x => x.Picture5Id, storeScope);

            if (model.Text5_OverrideForStore || storeScope == 0)
                _settingService.SaveSetting(BannerSliderSettings, x => x.Text5, storeScope, false);
            else if (storeScope > 0)
                _settingService.DeleteSetting(BannerSliderSettings, x => x.Text5, storeScope);

            if (model.Link5_OverrideForStore || storeScope == 0)
                _settingService.SaveSetting(BannerSliderSettings, x => x.Link5, storeScope, false);
            else if (storeScope > 0)
                _settingService.DeleteSetting(BannerSliderSettings, x => x.Link5, storeScope);
            
            //now clear settings cache
            _settingService.ClearCache();
            
            return Configure();
        }

        [ChildActionOnly]
        public ActionResult PublicInfo(string widgetZone, object additionalData = null)
        {
            var BannerSliderSettings = _settingService.LoadSetting<BannerSliderSettings>(_storeContext.CurrentStore.Id);

            var model = new PublicInfoModel();
            model.Picture1Url = GetPictureUrl(BannerSliderSettings.Picture1Id);
            model.Text1 = BannerSliderSettings.Text1;
            model.Link1 = BannerSliderSettings.Link1;

            model.Picture2Url = GetPictureUrl(BannerSliderSettings.Picture2Id);
            model.Text2 = BannerSliderSettings.Text2;
            model.Link2 = BannerSliderSettings.Link2;

            model.Picture3Url = GetPictureUrl(BannerSliderSettings.Picture3Id);
            model.Text3 = BannerSliderSettings.Text3;
            model.Link3 = BannerSliderSettings.Link3;

            model.Picture4Url = GetPictureUrl(BannerSliderSettings.Picture4Id);
            model.Text4 = BannerSliderSettings.Text4;
            model.Link4 = BannerSliderSettings.Link4;

            model.Picture5Url = GetPictureUrl(BannerSliderSettings.Picture5Id);
            model.Text5 = BannerSliderSettings.Text5;
            model.Link5 = BannerSliderSettings.Link5;

            if (string.IsNullOrEmpty(model.Picture1Url) && string.IsNullOrEmpty(model.Picture2Url) &&
                string.IsNullOrEmpty(model.Picture3Url) && string.IsNullOrEmpty(model.Picture4Url) &&
                string.IsNullOrEmpty(model.Picture5Url))
                //no pictures uploaded
                return Content("");


            return View("~/Plugins/Widgets.BannerSlider/Views/WidgetsBannerSlider/PublicInfo.cshtml", model);
        }
    }
}