using System.Web.Mvc;
using Nop.Web.Framework.Security;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Blogs;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Forums;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Vendors;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Directory;
using Nop.Services.Events;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Media;
using Nop.Services.Security;
using Nop.Services.Seo;
using Nop.Services.Stores;
using Nop.Services.Tax;
using Nop.Services.Vendors;
using Nop.Web.Extensions;
using Nop.Web.Framework.Events;
using Nop.Web.Framework.Security;
using Nop.Web.Infrastructure.Cache;
using Nop.Web.Models.Catalog;
using Nop.Web.Models.Media;
using Nop.Services.Topics;
using Nop.Services.Messages;
using Nop.Web.Framework.Themes;
using Nop.Services.Forums;
using Nop.Core.Domain.Tax;
using Nop.Core.Domain;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.News;
using Nop.Core.Domain.Localization;
using Nop.Web.Framework.UI.Captcha;
using System.Collections.Generic;

namespace Nop.Web.Controllers
{
    public partial class StoreController : BasePublicController
    {
	#region Fields

        private readonly ICategoryService _categoryService;
        private readonly IManufacturerService _manufacturerService;
        private readonly IProductService _productService;
        private readonly IVendorService _vendorService;
        private readonly ICategoryTemplateService _categoryTemplateService;
        private readonly IManufacturerTemplateService _manufacturerTemplateService;
        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly ITaxService _taxService;
        private readonly ICurrencyService _currencyService;
        private readonly IPictureService _pictureService;
        private readonly ILocalizationService _localizationService;
        private readonly IPriceCalculationService _priceCalculationService;
        private readonly IPriceFormatter _priceFormatter;
        private readonly IWebHelper _webHelper;
        private readonly ISpecificationAttributeService _specificationAttributeService;
        private readonly IProductTagService _productTagService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IAclService _aclService;
        private readonly IStoreMappingService _storeMappingService;
        private readonly IPermissionService _permissionService;
        private readonly ICustomerActivityService _customerActivityService;
        private readonly IEventPublisher _eventPublisher;
        private readonly ISearchTermService _searchTermService;
        private readonly MediaSettings _mediaSettings;
        private readonly CatalogSettings _catalogSettings;
        private readonly VendorSettings _vendorSettings;
        private readonly BlogSettings _blogSettings;
        private readonly ForumSettings _forumSettings;
        private readonly ICacheManager _cacheManager;
        
        #endregion

		#region Constructors

        public StoreController(ICategoryService categoryService, 
            IManufacturerService manufacturerService,
            IProductService productService, 
            IVendorService vendorService,
            ICategoryTemplateService categoryTemplateService,
            IManufacturerTemplateService manufacturerTemplateService,
            IWorkContext workContext, 
            IStoreContext storeContext,
            ITaxService taxService, 
            ICurrencyService currencyService,
            IPictureService pictureService, 
            ILocalizationService localizationService,
            IPriceCalculationService priceCalculationService,
            IPriceFormatter priceFormatter,
            IWebHelper webHelper, 
            ISpecificationAttributeService specificationAttributeService,
            IProductTagService productTagService,
            IGenericAttributeService genericAttributeService,
            IAclService aclService,
            IStoreMappingService storeMappingService,
            IPermissionService permissionService, 
            ICustomerActivityService customerActivityService,
            IEventPublisher eventPublisher,
            ISearchTermService searchTermService,
            MediaSettings mediaSettings,
            CatalogSettings catalogSettings,
            VendorSettings vendorSettings,
            BlogSettings blogSettings,
            ForumSettings  forumSettings,
            ICacheManager cacheManager)
        {
            this._categoryService = categoryService;
            this._manufacturerService = manufacturerService;
            this._productService = productService;
            this._vendorService = vendorService;
            this._categoryTemplateService = categoryTemplateService;
            this._manufacturerTemplateService = manufacturerTemplateService;
            this._workContext = workContext;
            this._storeContext = storeContext;
            this._taxService = taxService;
            this._currencyService = currencyService;
            this._pictureService = pictureService;
            this._localizationService = localizationService;
            this._priceCalculationService = priceCalculationService;
            this._priceFormatter = priceFormatter;
            this._webHelper = webHelper;
            this._specificationAttributeService = specificationAttributeService;
            this._productTagService = productTagService;
            this._genericAttributeService = genericAttributeService;
            this._aclService = aclService;
            this._storeMappingService = storeMappingService;
            this._permissionService = permissionService;
            this._customerActivityService = customerActivityService;
            this._eventPublisher = eventPublisher;
            this._searchTermService = searchTermService;
            this._mediaSettings = mediaSettings;
            this._catalogSettings = catalogSettings;
            this._vendorSettings = vendorSettings;
            this._blogSettings = blogSettings;
            this._forumSettings = forumSettings;
            this._cacheManager = cacheManager;
        }

        #endregion
        [NopHttpsRequirement(SslRequirement.No)]
        public ActionResult Index()
        {
            var categories = _categoryService.GetAllCategories();
            List<Product> products = new List<Product>();
            foreach (var category in categories)
            {
                products.AddRange( _productService.GetCategoryProducts(category.Id));
            }
            var ps = ControllerExtensions.PrepareProductOverviewModels(this, _workContext,
            _storeContext,
            _categoryService,
            _productService,
            _specificationAttributeService,
            _priceCalculationService,
            _priceFormatter,
            _permissionService,
            _localizationService,
            _taxService,
            _currencyService,
            _pictureService,
            _webHelper,
            _cacheManager,
            _catalogSettings,
            _mediaSettings,
            products);
            ViewBag.products = ps;
            return View();
        }
    }
}
