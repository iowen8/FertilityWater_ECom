using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Discounts;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Shipping;
using Nop.Core.Plugins;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Shipping;
using Nop.Services.Stores;
using Nop.Services.Tax;
using Nop.Web.Extensions;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Security;
using Nop.Web.Models.Checkout;
using Nop.Web.Models.Common;
using PayPal;
using PayPal.Api.Payments;
using Nop.Web.Models.ShoppingCart;  
namespace Nop.Web.Controllers
{
    [NopHttpsRequirement(SslRequirement.Yes)]
    public partial class CheckoutController : BasePublicController
    {
		#region Fields

        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly IStoreMappingService _storeMappingService;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly ILocalizationService _localizationService;
        private readonly ITaxService _taxService;
        private readonly ICurrencyService _currencyService;
        private readonly IPriceFormatter _priceFormatter;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly ICustomerService _customerService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly ICountryService _countryService;
        private readonly IStateProvinceService _stateProvinceService;
        private readonly IShippingService _shippingService;
        private readonly IPaymentService _paymentService;
        private readonly IPluginFinder _pluginFinder;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly ILogger _logger;
        private readonly IOrderService _orderService;
        private readonly IWebHelper _webHelper;
        private readonly HttpContextBase _httpContext;
        

        private readonly OrderSettings _orderSettings;
        private readonly RewardPointsSettings _rewardPointsSettings;
        private readonly PaymentSettings _paymentSettings;
        private readonly ShippingSettings _shippingSettings;
        private readonly AddressSettings _addressSettings;

        #endregion

		#region Constructors

        public CheckoutController(IWorkContext workContext,
            IStoreContext storeContext,
            IStoreMappingService storeMappingService,
            IShoppingCartService shoppingCartService, 
            ILocalizationService localizationService, 
            ITaxService taxService, 
            ICurrencyService currencyService, 
            IPriceFormatter priceFormatter, 
            IOrderProcessingService orderProcessingService,
            ICustomerService customerService, 
            IGenericAttributeService genericAttributeService,
            ICountryService countryService,
            IStateProvinceService stateProvinceService,
            IShippingService shippingService, 
            IPaymentService paymentService,
            IPluginFinder pluginFinder,
            IOrderTotalCalculationService orderTotalCalculationService,
            ILogger logger,
            IOrderService orderService,
            IWebHelper webHelper,
            HttpContextBase httpContext,
            OrderSettings orderSettings, 
            RewardPointsSettings rewardPointsSettings,
            PaymentSettings paymentSettings,
            ShippingSettings shippingSettings,
            AddressSettings addressSettings)
        {
            this._workContext = workContext;
            this._storeContext = storeContext;
            this._storeMappingService = storeMappingService;
            this._shoppingCartService = shoppingCartService;
            this._localizationService = localizationService;
            this._taxService = taxService;
            this._currencyService = currencyService;
            this._priceFormatter = priceFormatter;
            this._orderProcessingService = orderProcessingService;
            this._customerService = customerService;
            this._genericAttributeService = genericAttributeService;
            this._countryService = countryService;
            this._stateProvinceService = stateProvinceService;
            this._shippingService = shippingService;
            this._paymentService = paymentService;
            this._pluginFinder = pluginFinder;
            this._orderTotalCalculationService = orderTotalCalculationService;
            this._logger = logger;
            this._orderService = orderService;
            this._webHelper = webHelper;
            this._httpContext = httpContext;

            this._orderSettings = orderSettings;
            this._rewardPointsSettings = rewardPointsSettings;
            this._paymentSettings = paymentSettings;
            this._shippingSettings = shippingSettings;
            this._addressSettings = addressSettings;
        }

        #endregion

        #region Utilities

        [NonAction]
        protected virtual bool IsPaymentWorkflowRequired(IList<ShoppingCartItem> cart, bool ignoreRewardPoints = false)
        {
            bool result = true;

            //check whether order total equals zero
            decimal? shoppingCartTotalBase = _orderTotalCalculationService.GetShoppingCartTotal(cart, ignoreRewardPoints);
            if (shoppingCartTotalBase.HasValue && shoppingCartTotalBase.Value == decimal.Zero)
                result = false;
            return result;
        }

        [NonAction]
        protected virtual CheckoutBillingAddressModel PrepareBillingAddressModel(int? selectedCountryId = null, 
            bool prePopulateNewAddressWithCustomerFields = false)
        {
            var model = new CheckoutBillingAddressModel();
            //existing addresses
            var addresses = _workContext.CurrentCustomer.Addresses
                //allow billing
                .Where(a => a.Country == null || a.Country.AllowsBilling)
                //enabled for the current store
                .Where(a => a.Country == null || _storeMappingService.Authorize(a.Country))
                .ToList();
            foreach (var address in addresses)
            {
                var addressModel = new AddressModel();
                addressModel.PrepareModel(address, 
                    false, 
                    _addressSettings);
                model.ExistingAddresses.Add(addressModel);
            }

            //new address
            model.NewAddress.CountryId = selectedCountryId;
            model.NewAddress.PrepareModel(null,
                false,
                _addressSettings,
                _localizationService,
                _stateProvinceService,
                () => _countryService.GetAllCountriesForBilling(),
                prePopulateNewAddressWithCustomerFields,
                _workContext.CurrentCustomer);
            return model;
        }

        [NonAction]
        protected virtual CheckoutShippingAddressModel PrepareShippingAddressModel(int? selectedCountryId = null, 
            bool prePopulateNewAddressWithCustomerFields = false)
        {
            var model = new CheckoutShippingAddressModel();
            //allow pickup in store?
            model.AllowPickUpInStore = _shippingSettings.AllowPickUpInStore;
            //existing addresses
            var addresses = _workContext.CurrentCustomer.Addresses
                //allow shipping
                .Where(a => a.Country == null || a.Country.AllowsShipping)
                //enabled for the current store
                .Where(a => a.Country == null || _storeMappingService.Authorize(a.Country))
                .ToList();
            foreach (var address in addresses)
            {
                var addressModel = new AddressModel();
                addressModel.PrepareModel(address,
                    false,
                    _addressSettings);
                model.ExistingAddresses.Add(addressModel);
            }

            //new address
            model.NewAddress.CountryId = selectedCountryId;
            model.NewAddress.PrepareModel(null,
                false,
                _addressSettings,
                _localizationService,
                _stateProvinceService,
                () => _countryService.GetAllCountriesForShipping(),
                prePopulateNewAddressWithCustomerFields,
                _workContext.CurrentCustomer);
            return model;
        }

        [NonAction]
        protected virtual CheckoutShippingMethodModel PrepareShippingMethodModel(IList<ShoppingCartItem> cart)
        {
            var model = new CheckoutShippingMethodModel();

            var getShippingOptionResponse = _shippingService
                .GetShippingOptions(cart, _workContext.CurrentCustomer.ShippingAddress,
                "", _storeContext.CurrentStore.Id);
            if (getShippingOptionResponse.Success)
            {
                //performance optimization. cache returned shipping options.
                //we'll use them later (after a customer has selected an option).
                _genericAttributeService.SaveAttribute(_workContext.CurrentCustomer,
                                                       SystemCustomerAttributeNames.OfferedShippingOptions,
                                                       getShippingOptionResponse.ShippingOptions,
                                                       _storeContext.CurrentStore.Id);

                foreach (var shippingOption in getShippingOptionResponse.ShippingOptions)
                {
                    var soModel = new CheckoutShippingMethodModel.ShippingMethodModel()
                                      {
                                          Name = shippingOption.Name,
                                          Description = shippingOption.Description,
                                          ShippingRateComputationMethodSystemName = shippingOption.ShippingRateComputationMethodSystemName,
                                          ShippingOption = shippingOption,
                                      };

                    //adjust rate
                    Discount appliedDiscount = null;
                    var shippingTotal = _orderTotalCalculationService.AdjustShippingRate(
                        shippingOption.Rate, cart, out appliedDiscount);

                    decimal rateBase = _taxService.GetShippingPrice(shippingTotal, _workContext.CurrentCustomer);
                    decimal rate = _currencyService.ConvertFromPrimaryStoreCurrency(rateBase,
                                                                                    _workContext.WorkingCurrency);
                    soModel.Fee = _priceFormatter.FormatShippingPrice(rate, true);

                    model.ShippingMethods.Add(soModel);
                }

                //find a selected (previously) shipping method
                var selectedShippingOption = _workContext.CurrentCustomer.GetAttribute<ShippingOption>(
                        SystemCustomerAttributeNames.SelectedShippingOption, _storeContext.CurrentStore.Id);
                if (selectedShippingOption != null)
                {
                    var shippingOptionToSelect = model.ShippingMethods.ToList()
                        .Find( so =>
                            !String.IsNullOrEmpty(so.Name) &&
                            so.Name.Equals(selectedShippingOption.Name, StringComparison.InvariantCultureIgnoreCase) &&
                            !String.IsNullOrEmpty(so.ShippingRateComputationMethodSystemName) &&
                            so.ShippingRateComputationMethodSystemName.Equals(selectedShippingOption.ShippingRateComputationMethodSystemName, StringComparison.InvariantCultureIgnoreCase));
                    if (shippingOptionToSelect != null)
                    {
                        shippingOptionToSelect.Selected = true;
                    }
                }
                //if no option has been selected, let's do it for the first one
                if (model.ShippingMethods.FirstOrDefault(so => so.Selected) == null)
                {
                    var shippingOptionToSelect = model.ShippingMethods.FirstOrDefault();
                    if (shippingOptionToSelect != null)
                    {
                        shippingOptionToSelect.Selected = true;
                    }
                }
            }
            else
            {
                foreach (var error in getShippingOptionResponse.Errors)
                    model.Warnings.Add(error);
            }

            return model;
        }

        [NonAction]
        protected virtual CheckoutPaymentMethodModel PreparePaymentMethodModel(IList<ShoppingCartItem> cart)
        {
            var model = new CheckoutPaymentMethodModel();

            //reward points
            if (_rewardPointsSettings.Enabled && !cart.IsRecurring())
            {
                int rewardPointsBalance = _workContext.CurrentCustomer.GetRewardPointsBalance();
                decimal rewardPointsAmountBase = _orderTotalCalculationService.ConvertRewardPointsToAmount(rewardPointsBalance);
                decimal rewardPointsAmount = _currencyService.ConvertFromPrimaryStoreCurrency(rewardPointsAmountBase, _workContext.WorkingCurrency);
                if (rewardPointsAmount > decimal.Zero && 
                    _orderTotalCalculationService.CheckMinimumRewardPointsToUseRequirement(rewardPointsBalance))
                {
                    model.DisplayRewardPoints = true;
                    model.RewardPointsAmount = _priceFormatter.FormatPrice(rewardPointsAmount, true, false);
                    model.RewardPointsBalance = rewardPointsBalance;
                }
            }

            //filter by country
            int filterByCountryId = 0;
            if (_addressSettings.CountryEnabled &&
                _workContext.CurrentCustomer.BillingAddress != null &&
                _workContext.CurrentCustomer.BillingAddress.Country != null)
            {
                filterByCountryId = _workContext.CurrentCustomer.BillingAddress.Country.Id;
            }

            var boundPaymentMethods = _paymentService
                .LoadActivePaymentMethods(_workContext.CurrentCustomer.Id, _storeContext.CurrentStore.Id, filterByCountryId)
                .Where(pm => pm.PaymentMethodType == PaymentMethodType.Standard || pm.PaymentMethodType == PaymentMethodType.Redirection)
                .ToList();
            foreach (var pm in boundPaymentMethods)
            {
                if (cart.IsRecurring() && pm.RecurringPaymentType == RecurringPaymentType.NotSupported)
                    continue;

                var pmModel = new CheckoutPaymentMethodModel.PaymentMethodModel()
                {
                    Name = pm.GetLocalizedFriendlyName(_localizationService, _workContext.WorkingLanguage.Id),
                    PaymentMethodSystemName = pm.PluginDescriptor.SystemName,
                    LogoUrl = pm.PluginDescriptor.GetLogoUrl(_webHelper)
                };
                //payment method additional fee
                decimal paymentMethodAdditionalFee = _paymentService.GetAdditionalHandlingFee(cart, pm.PluginDescriptor.SystemName);
                decimal rateBase = _taxService.GetPaymentMethodAdditionalFee(paymentMethodAdditionalFee, _workContext.CurrentCustomer);
                decimal rate = _currencyService.ConvertFromPrimaryStoreCurrency(rateBase, _workContext.WorkingCurrency);
                if (rate > decimal.Zero)
                    pmModel.Fee = _priceFormatter.FormatPaymentMethodAdditionalFee(rate, true);

                model.PaymentMethods.Add(pmModel);
            }
            
            //find a selected (previously) payment method
            var selectedPaymentMethodSystemName = _workContext.CurrentCustomer.GetAttribute<string>(
                SystemCustomerAttributeNames.SelectedPaymentMethod,
                _genericAttributeService, _storeContext.CurrentStore.Id);
            if (!String.IsNullOrEmpty(selectedPaymentMethodSystemName))
            {
                var paymentMethodToSelect = model.PaymentMethods.ToList()
                    .Find(pm => pm.PaymentMethodSystemName.Equals(selectedPaymentMethodSystemName, StringComparison.InvariantCultureIgnoreCase));
                if (paymentMethodToSelect != null)
                    paymentMethodToSelect.Selected = true;
            }
            //if no option has been selected, let's do it for the first one
            if (model.PaymentMethods.FirstOrDefault(so => so.Selected) == null)
            {
                var paymentMethodToSelect = model.PaymentMethods.FirstOrDefault();
                if (paymentMethodToSelect != null)
                    paymentMethodToSelect.Selected = true;
            }

            return model;
        }

        [NonAction]
        protected virtual CheckoutPaymentInfoModel PreparePaymentInfoModel(IPaymentMethod paymentMethod)
        {
            var model = new CheckoutPaymentInfoModel();
            string actionName;
            string controllerName;
            RouteValueDictionary routeValues;
            paymentMethod.GetPaymentInfoRoute(out actionName, out controllerName, out routeValues);
            model.PaymentInfoActionName = actionName;
            model.PaymentInfoControllerName = controllerName;
            model.PaymentInfoRouteValues = routeValues;
            model.DisplayOrderTotals = _orderSettings.OnePageCheckoutDisplayOrderTotalsOnPaymentInfoTab;
            return model;
        }

        [NonAction]
        protected virtual CheckoutConfirmModel PrepareConfirmOrderModel(IList<ShoppingCartItem> cart)
        {
            var model = new CheckoutConfirmModel();
            //terms of service
            model.TermsOfServiceOnOrderConfirmPage = _orderSettings.TermsOfServiceOnOrderConfirmPage;
            //min order amount validation
            bool minOrderTotalAmountOk = _orderProcessingService.ValidateMinOrderTotalAmount(cart);
            if (!minOrderTotalAmountOk)
            {
                decimal minOrderTotalAmount = _currencyService.ConvertFromPrimaryStoreCurrency(_orderSettings.MinOrderTotalAmount, _workContext.WorkingCurrency);
                model.MinOrderTotalWarning = string.Format(_localizationService.GetResource("Checkout.MinOrderTotalAmount"), _priceFormatter.FormatPrice(minOrderTotalAmount, true, false));
            }
            return model;
        }
        
        [NonAction]
        protected virtual bool IsMinimumOrderPlacementIntervalValid(Customer customer)
        {
            //prevent 2 orders being placed within an X seconds time frame
            if (_orderSettings.MinimumOrderPlacementInterval == 0)
                return true;

            var lastOrder = _orderService.SearchOrders(storeId: _storeContext.CurrentStore.Id,
                customerId: _workContext.CurrentCustomer.Id, pageSize: 1)
                .FirstOrDefault();
            if (lastOrder == null)
                return true;

            var interval = DateTime.UtcNow - lastOrder.CreatedOnUtc;
            return interval.TotalSeconds > _orderSettings.MinimumOrderPlacementInterval;
        }

        #endregion

        #region Methods (common)

        public ActionResult Index()
        {
            var cart = _workContext.CurrentCustomer.ShoppingCartItems
                .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                .LimitPerStore(_storeContext.CurrentStore.Id)
                .ToList();
            if (cart.Count == 0)
                return RedirectToRoute("ShoppingCart");

            if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                return new HttpUnauthorizedResult();

            //reset checkout data
            _customerService.ResetCheckoutData(_workContext.CurrentCustomer, _storeContext.CurrentStore.Id);

            //validation (cart)
            var checkoutAttributesXml = _workContext.CurrentCustomer.GetAttribute<string>(SystemCustomerAttributeNames.CheckoutAttributes, _genericAttributeService, _storeContext.CurrentStore.Id);
            var scWarnings = _shoppingCartService.GetShoppingCartWarnings(cart, checkoutAttributesXml, true);
            if (scWarnings.Count > 0)
                return RedirectToRoute("ShoppingCart");
            //validation (each shopping cart item)
            foreach (ShoppingCartItem sci in cart)
            {
                var sciWarnings = _shoppingCartService.GetShoppingCartItemWarnings(_workContext.CurrentCustomer,
                    sci.ShoppingCartType,
                    sci.Product,
                    sci.StoreId,
                    sci.AttributesXml,
                    sci.CustomerEnteredPrice,
                    sci.Quantity,
                    false);
                if (sciWarnings.Count > 0)
                    return RedirectToRoute("ShoppingCart");
            }

            if (_orderSettings.OnePageCheckoutEnabled)
                return RedirectToRoute("CheckoutOnePage");
            else
                return RedirectToRoute("CheckoutBillingAddress");
        }

        public ActionResult Completed(int? orderId)
        {
            //validation
            if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                return new HttpUnauthorizedResult();

           Nop.Core.Domain.Orders.Order order = null;
            if (orderId.HasValue)
            {
                //load order by identifier (if provided)
                order = _orderService.GetOrderById(orderId.Value);
            }
            if (order == null)
            {
                order = _orderService.SearchOrders(storeId: _storeContext.CurrentStore.Id,
                customerId: _workContext.CurrentCustomer.Id, pageSize: 1)
                    .FirstOrDefault();
            }
            if (order == null || order.Deleted || _workContext.CurrentCustomer.Id != order.CustomerId)
            {
                return RedirectToRoute("HomePage");
            }

            //disable "order completed" page?
            if (_orderSettings.DisableOrderCompletedPage)
            {
                return RedirectToRoute("OrderDetails", new {orderId = order.Id});
            }

            //model
            var model = new CheckoutCompletedModel()
            {
                OrderId = order.Id,
                OnePageCheckoutEnabled = _orderSettings.OnePageCheckoutEnabled
            };

            return View(model);
        }

        #endregion

        #region Methods (multistep checkout)

        public ActionResult BillingAddress()
        {
            //validation
            var cart = _workContext.CurrentCustomer.ShoppingCartItems
                .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                .LimitPerStore(_storeContext.CurrentStore.Id)
                .ToList();
            if (cart.Count == 0)
                return RedirectToRoute("ShoppingCart");

            if (_orderSettings.OnePageCheckoutEnabled)
                return RedirectToRoute("CheckoutOnePage");

            if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                return new HttpUnauthorizedResult();

            //model
            var model = PrepareBillingAddressModel(prePopulateNewAddressWithCustomerFields: true);

            //check whether "billing address" step is enabled
            if (_orderSettings.DisableBillingAddressCheckoutStep)
            {
                if (model.ExistingAddresses.Any())
                {
                    //choose the first one
                    return SelectBillingAddress(model.ExistingAddresses.First().Id);
                }
                else
                {
                    TryValidateModel(model);
                    TryValidateModel(model.NewAddress);
                    return NewBillingAddress(model);
                }
            }

            return View(model);
        }
        public ActionResult SelectBillingAddress(int addressId)
        {
            var address = _workContext.CurrentCustomer.Addresses.FirstOrDefault(a => a.Id == addressId);
            if (address == null)
                return RedirectToRoute("CheckoutBillingAddress");

            _workContext.CurrentCustomer.BillingAddress = address;
            _customerService.UpdateCustomer(_workContext.CurrentCustomer);

            return RedirectToRoute("CheckoutShippingAddress");
        }
        [HttpPost, ActionName("BillingAddress")]
        [FormValueRequired("nextstep")]
        public ActionResult NewBillingAddress(CheckoutBillingAddressModel model)
        {
            //validation
            var cart = _workContext.CurrentCustomer.ShoppingCartItems
                .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                .LimitPerStore(_storeContext.CurrentStore.Id)
                .ToList();
            if (cart.Count == 0)
                return RedirectToRoute("ShoppingCart");

            if (_orderSettings.OnePageCheckoutEnabled)
                return RedirectToRoute("CheckoutOnePage");

            if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                return new HttpUnauthorizedResult();

            if (ModelState.IsValid)
            {
                //try to find an address with the same values (don't duplicate records)
                var address = _workContext.CurrentCustomer.Addresses.ToList().FindAddress(
                    model.NewAddress.FirstName, model.NewAddress.LastName, model.NewAddress.PhoneNumber,
                    model.NewAddress.Email, model.NewAddress.FaxNumber, model.NewAddress.Company,
                    model.NewAddress.Address1, model.NewAddress.Address2, model.NewAddress.City,
                    model.NewAddress.StateProvinceId, model.NewAddress.ZipPostalCode, model.NewAddress.CountryId);
                if (address == null)
                {
                    //address is not found. let's create a new one
                    address = model.NewAddress.ToEntity();
                    address.CreatedOnUtc = DateTime.UtcNow;
                    //some validation
                    if (address.CountryId == 0)
                        address.CountryId = null;
                    if (address.StateProvinceId == 0)
                        address.StateProvinceId = null;
                    _workContext.CurrentCustomer.Addresses.Add(address);
                }
                _workContext.CurrentCustomer.BillingAddress = address;
                _customerService.UpdateCustomer(_workContext.CurrentCustomer);

                return RedirectToRoute("CheckoutShippingAddress");
            }


            //If we got this far, something failed, redisplay form
            model = PrepareBillingAddressModel(selectedCountryId: model.NewAddress.CountryId);
            return View(model);
        }

        public ActionResult ShippingAddress()
        {
            //validation
            var cart = _workContext.CurrentCustomer.ShoppingCartItems
                .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                .LimitPerStore(_storeContext.CurrentStore.Id)
                .ToList();
            if (cart.Count == 0)
                return RedirectToRoute("ShoppingCart");

            if (_orderSettings.OnePageCheckoutEnabled)
                return RedirectToRoute("CheckoutOnePage");

            if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                return new HttpUnauthorizedResult();

            if (!cart.RequiresShipping())
            {
                _workContext.CurrentCustomer.ShippingAddress = null;
                _customerService.UpdateCustomer(_workContext.CurrentCustomer);
                return RedirectToRoute("CheckoutShippingMethod");
            }

            //model
            var model = PrepareShippingAddressModel(prePopulateNewAddressWithCustomerFields: true);
            return View(model);
        }
        public ActionResult SelectShippingAddress(int addressId)
        {
            var address = _workContext.CurrentCustomer.Addresses.FirstOrDefault(a => a.Id == addressId);
            if (address == null)
                return RedirectToRoute("CheckoutShippingAddress");

            _workContext.CurrentCustomer.ShippingAddress = address;
            _customerService.UpdateCustomer(_workContext.CurrentCustomer);

            //Pick up in store?
            if (_shippingSettings.AllowPickUpInStore)
            {
                //set value indicating that "pick up in store" option has not been chosen
                _genericAttributeService.SaveAttribute(_workContext.CurrentCustomer, SystemCustomerAttributeNames.SelectedPickUpInStore, false, _storeContext.CurrentStore.Id);
            }

            return RedirectToRoute("CheckoutShippingMethod");
        }
        [HttpPost, ActionName("ShippingAddress")]
        [FormValueRequired("nextstep")]
        public ActionResult NewShippingAddress(CheckoutShippingAddressModel model)
        {
            //validation
            var cart = _workContext.CurrentCustomer.ShoppingCartItems
                .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                .LimitPerStore(_storeContext.CurrentStore.Id)
                .ToList();
            if (cart.Count == 0)
                return RedirectToRoute("ShoppingCart");

            if (_orderSettings.OnePageCheckoutEnabled)
                return RedirectToRoute("CheckoutOnePage");

            if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                return new HttpUnauthorizedResult();

            if (!cart.RequiresShipping())
            {
                _workContext.CurrentCustomer.ShippingAddress = null;
                _customerService.UpdateCustomer(_workContext.CurrentCustomer);
                return RedirectToRoute("CheckoutShippingMethod");
            }


            //Pick up in store?
            if (_shippingSettings.AllowPickUpInStore)
            {
                if (model.PickUpInStore)
                {
                    //customer decided to pick up in store

                    //no shipping address selected
                    _workContext.CurrentCustomer.ShippingAddress = null;
                    _customerService.UpdateCustomer(_workContext.CurrentCustomer);

                    //set value indicating that "pick up in store" option has been chosen
                    _genericAttributeService.SaveAttribute(_workContext.CurrentCustomer, SystemCustomerAttributeNames.SelectedPickUpInStore, true, _storeContext.CurrentStore.Id);

                    //save "pick up in store" shipping method
                    var pickUpInStoreShippingOption = new ShippingOption()
                    {
                        Name = _localizationService.GetResource("Checkout.PickUpInStore.MethodName"),
                        Rate = decimal.Zero,
                        Description = null,
                        ShippingRateComputationMethodSystemName = null
                    };
                    _genericAttributeService.SaveAttribute(_workContext.CurrentCustomer,
                        SystemCustomerAttributeNames.SelectedShippingOption,
                        pickUpInStoreShippingOption,
                        _storeContext.CurrentStore.Id);

                    //load next step
                    return RedirectToRoute("CheckoutShippingMethod");
                }

                //set value indicating that "pick up in store" option has not been chosen
                _genericAttributeService.SaveAttribute(_workContext.CurrentCustomer, SystemCustomerAttributeNames.SelectedPickUpInStore, false, _storeContext.CurrentStore.Id);
            }


            if (ModelState.IsValid)
            {
                //try to find an address with the same values (don't duplicate records)
                var address = _workContext.CurrentCustomer.Addresses.ToList().FindAddress(
                    model.NewAddress.FirstName, model.NewAddress.LastName, model.NewAddress.PhoneNumber,
                    model.NewAddress.Email, model.NewAddress.FaxNumber, model.NewAddress.Company,
                    model.NewAddress.Address1, model.NewAddress.Address2, model.NewAddress.City,
                    model.NewAddress.StateProvinceId, model.NewAddress.ZipPostalCode, model.NewAddress.CountryId);
                if (address == null)
                {
                    address = model.NewAddress.ToEntity();
                    address.CreatedOnUtc = DateTime.UtcNow;
                    //some validation
                    if (address.CountryId == 0)
                        address.CountryId = null;
                    if (address.StateProvinceId == 0)
                        address.StateProvinceId = null;
                    _workContext.CurrentCustomer.Addresses.Add(address);
                }
                _workContext.CurrentCustomer.ShippingAddress = address;
                _customerService.UpdateCustomer(_workContext.CurrentCustomer);

                return RedirectToRoute("CheckoutShippingMethod");
            }


            //If we got this far, something failed, redisplay form
            model = PrepareShippingAddressModel(selectedCountryId: model.NewAddress.CountryId);
            return View(model);
        }
        

        public ActionResult ShippingMethod()
        {
            //validation
            var cart = _workContext.CurrentCustomer.ShoppingCartItems
                .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                .LimitPerStore(_storeContext.CurrentStore.Id)
                .ToList();
            if (cart.Count == 0)
                return RedirectToRoute("ShoppingCart");

            if (_orderSettings.OnePageCheckoutEnabled)
                return RedirectToRoute("CheckoutOnePage");

            if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                return new HttpUnauthorizedResult();

            if (!cart.RequiresShipping())
            {
                _genericAttributeService.SaveAttribute<ShippingOption>(_workContext.CurrentCustomer, SystemCustomerAttributeNames.SelectedShippingOption, null, _storeContext.CurrentStore.Id);
                return RedirectToRoute("CheckoutPaymentMethod");
            }

            if (_shippingSettings.AllowPickUpInStore)
            {
                //customer decided to pick up in store?
                var pickUpInStore = _workContext.CurrentCustomer.GetAttribute<bool>(SystemCustomerAttributeNames.SelectedPickUpInStore, 
                    _storeContext.CurrentStore.Id);
                if (pickUpInStore)
                {
                    return RedirectToRoute("CheckoutPaymentMethod");
                }
            }
            
            //model
            var model = PrepareShippingMethodModel(cart);

            if (_shippingSettings.BypassShippingMethodSelectionIfOnlyOne &&
                model.ShippingMethods.Count == 1)
            {
                //if we have only one shipping method, then a customer doesn't have to choose a shipping method
                _genericAttributeService.SaveAttribute(_workContext.CurrentCustomer, 
                    SystemCustomerAttributeNames.SelectedShippingOption,
                    model.ShippingMethods.First().ShippingOption,
                    _storeContext.CurrentStore.Id);
            
                return RedirectToRoute("CheckoutPaymentMethod");
            }

            return View(model);
        }
        [HttpPost, ActionName("ShippingMethod")]
        [FormValueRequired("nextstep")]
        [ValidateInput(false)]
        public ActionResult SelectShippingMethod(string shippingoption)
        {
            //validation
            var cart = _workContext.CurrentCustomer.ShoppingCartItems
                .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                .LimitPerStore(_storeContext.CurrentStore.Id)
                .ToList();
            if (cart.Count == 0)
                return RedirectToRoute("ShoppingCart");

            if (_orderSettings.OnePageCheckoutEnabled)
                return RedirectToRoute("CheckoutOnePage");

            if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                return new HttpUnauthorizedResult();

            if (!cart.RequiresShipping())
            {
                _genericAttributeService.SaveAttribute<ShippingOption>(_workContext.CurrentCustomer,
                    SystemCustomerAttributeNames.SelectedShippingOption, null, _storeContext.CurrentStore.Id);
                return RedirectToRoute("CheckoutPaymentMethod");
            }

            //parse selected method 
            if (String.IsNullOrEmpty(shippingoption))
                return ShippingMethod();
            var splittedOption = shippingoption.Split(new string[] { "___" }, StringSplitOptions.RemoveEmptyEntries);
            if (splittedOption.Length != 2)
                return ShippingMethod();
            string selectedName = splittedOption[0];
            string shippingRateComputationMethodSystemName = splittedOption[1];
            
            //find it
            //performance optimization. try cache first
            var shippingOptions = _workContext.CurrentCustomer.GetAttribute<List<ShippingOption>>(SystemCustomerAttributeNames.OfferedShippingOptions, _storeContext.CurrentStore.Id);
            if (shippingOptions == null || shippingOptions.Count == 0)
            {
                //not found? let's load them using shipping service
                shippingOptions = _shippingService
                    .GetShippingOptions(cart, _workContext.CurrentCustomer.ShippingAddress, shippingRateComputationMethodSystemName, _storeContext.CurrentStore.Id)
                    .ShippingOptions
                    .ToList();
            }
            else
            {
                //loaded cached results. let's filter result by a chosen shipping rate computation method
                shippingOptions = shippingOptions.Where(so => so.ShippingRateComputationMethodSystemName.Equals(shippingRateComputationMethodSystemName, StringComparison.InvariantCultureIgnoreCase))
                    .ToList();
            }

            var shippingOption = shippingOptions
                .Find(so => !String.IsNullOrEmpty(so.Name) && so.Name.Equals(selectedName, StringComparison.InvariantCultureIgnoreCase));
            if (shippingOption == null)
                return ShippingMethod();

            //save
            _genericAttributeService.SaveAttribute(_workContext.CurrentCustomer, SystemCustomerAttributeNames.SelectedShippingOption, shippingOption, _storeContext.CurrentStore.Id);
            
            return RedirectToRoute("CheckoutPaymentMethod");
        }
        
        
        public ActionResult PaymentMethod()
        {
            //validation
            var cart = _workContext.CurrentCustomer.ShoppingCartItems
                .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                .LimitPerStore(_storeContext.CurrentStore.Id)
                .ToList();
            if (cart.Count == 0)
                return RedirectToRoute("ShoppingCart");

            if (_orderSettings.OnePageCheckoutEnabled)
                return RedirectToRoute("CheckoutOnePage");

            if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                return new HttpUnauthorizedResult();

            //Check whether payment workflow is required
            //we ignore reward points during cart total calculation
            bool isPaymentWorkflowRequired = IsPaymentWorkflowRequired(cart, true);
            if (!isPaymentWorkflowRequired)
            {
                _genericAttributeService.SaveAttribute<string>(_workContext.CurrentCustomer,
                    SystemCustomerAttributeNames.SelectedPaymentMethod, null, _storeContext.CurrentStore.Id);
                return RedirectToRoute("CheckoutPaymentInfo");
            }

            //model
            var paymentMethodModel = PreparePaymentMethodModel(cart);

            if (_paymentSettings.BypassPaymentMethodSelectionIfOnlyOne &&
                paymentMethodModel.PaymentMethods.Count == 1 && !paymentMethodModel.DisplayRewardPoints)
            {
                //if we have only one payment method and reward points are disabled or the current customer doesn't have any reward points
                //so customer doesn't have to choose a payment method

                _genericAttributeService.SaveAttribute<string>(_workContext.CurrentCustomer,
                    SystemCustomerAttributeNames.SelectedPaymentMethod, 
                    paymentMethodModel.PaymentMethods[0].PaymentMethodSystemName,
                    _storeContext.CurrentStore.Id);
                return RedirectToRoute("CheckoutPaymentInfo");
            }

            return View(paymentMethodModel);
        }
        [HttpPost, ActionName("PaymentMethod")]
        [FormValueRequired("nextstep")]
        [ValidateInput(false)]
        public ActionResult SelectPaymentMethod(string paymentmethod, CheckoutPaymentMethodModel model)
        {
            //validation
            var cart = _workContext.CurrentCustomer.ShoppingCartItems
                .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                .LimitPerStore(_storeContext.CurrentStore.Id)
                .ToList();
            if (cart.Count == 0)
                return RedirectToRoute("ShoppingCart");

            if (_orderSettings.OnePageCheckoutEnabled)
                return RedirectToRoute("CheckoutOnePage");

            if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                return new HttpUnauthorizedResult();

            //reward points
            if (_rewardPointsSettings.Enabled)
            {
                _genericAttributeService.SaveAttribute(_workContext.CurrentCustomer,
                    SystemCustomerAttributeNames.UseRewardPointsDuringCheckout, model.UseRewardPoints,
                    _storeContext.CurrentStore.Id);
            }

            //Check whether payment workflow is required
            bool isPaymentWorkflowRequired = IsPaymentWorkflowRequired(cart);
            if (!isPaymentWorkflowRequired)
            {
                _genericAttributeService.SaveAttribute<string>(_workContext.CurrentCustomer,
                    SystemCustomerAttributeNames.SelectedPaymentMethod, null, _storeContext.CurrentStore.Id);
                return RedirectToRoute("CheckoutPaymentInfo");
            }
            //payment method 
            if (String.IsNullOrEmpty(paymentmethod))
                return PaymentMethod();

            var paymentMethodInst = _paymentService.LoadPaymentMethodBySystemName(paymentmethod);
            if (paymentMethodInst == null || 
                !paymentMethodInst.IsPaymentMethodActive(_paymentSettings) ||
                !_pluginFinder.AuthenticateStore(paymentMethodInst.PluginDescriptor, _storeContext.CurrentStore.Id))
                return PaymentMethod();

            //save
            _genericAttributeService.SaveAttribute<string>(_workContext.CurrentCustomer,
                SystemCustomerAttributeNames.SelectedPaymentMethod, paymentmethod, _storeContext.CurrentStore.Id);
            
            return RedirectToRoute("CheckoutPaymentInfo");
        }


        public ActionResult PaymentInfo()
        {
            //validation
            var cart = _workContext.CurrentCustomer.ShoppingCartItems
                .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                .LimitPerStore(_storeContext.CurrentStore.Id)
                .ToList();
            if (cart.Count == 0)
                return RedirectToRoute("ShoppingCart");

            if (_orderSettings.OnePageCheckoutEnabled)
                return RedirectToRoute("CheckoutOnePage");

            if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                return new HttpUnauthorizedResult();

            //Check whether payment workflow is required
            bool isPaymentWorkflowRequired = IsPaymentWorkflowRequired(cart);
            if (!isPaymentWorkflowRequired)
            {
                return RedirectToRoute("CheckoutConfirm");
            }

            //load payment method
            var paymentMethodSystemName = _workContext.CurrentCustomer.GetAttribute<string>(
                SystemCustomerAttributeNames.SelectedPaymentMethod,
                _genericAttributeService, _storeContext.CurrentStore.Id);
            var paymentMethod = _paymentService.LoadPaymentMethodBySystemName(paymentMethodSystemName);
            if (paymentMethod == null)
                return RedirectToRoute("CheckoutPaymentMethod");

            //Check whether payment info should be skipped
            if (paymentMethod.SkipPaymentInfo)
            {
                //skip payment info page
                var paymentInfo = new ProcessPaymentRequest();
                //session save
                _httpContext.Session["OrderPaymentInfo"] = paymentInfo;

                return RedirectToRoute("CheckoutConfirm");
            }

            //model
            var model = PreparePaymentInfoModel(paymentMethod);
            return View(model);
        }
        [HttpPost, ActionName("PaymentInfo")]
        [FormValueRequired("nextstep")]
        [ValidateInput(false)]
        public ActionResult EnterPaymentInfo(FormCollection form)
        {
            //validation
            var cart = _workContext.CurrentCustomer.ShoppingCartItems
                .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                .LimitPerStore(_storeContext.CurrentStore.Id)
                .ToList();
            if (cart.Count == 0)
                return RedirectToRoute("ShoppingCart");

            if (_orderSettings.OnePageCheckoutEnabled)
                return RedirectToRoute("CheckoutOnePage");

            if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                return new HttpUnauthorizedResult();

            //Check whether payment workflow is required
            bool isPaymentWorkflowRequired = IsPaymentWorkflowRequired(cart);
            if (!isPaymentWorkflowRequired)
            {
                return RedirectToRoute("CheckoutConfirm");
            }

            //load payment method
            var paymentMethodSystemName = _workContext.CurrentCustomer.GetAttribute<string>(
                SystemCustomerAttributeNames.SelectedPaymentMethod,
                _genericAttributeService, _storeContext.CurrentStore.Id);
            var paymentMethod = _paymentService.LoadPaymentMethodBySystemName(paymentMethodSystemName);
            if (paymentMethod == null)
                return RedirectToRoute("CheckoutPaymentMethod");

            var paymentControllerType = paymentMethod.GetControllerType();
            var paymentController = DependencyResolver.Current.GetService(paymentControllerType) as BasePaymentController;
            var warnings = paymentController.ValidatePaymentForm(form);
            foreach (var warning in warnings)
                ModelState.AddModelError("", warning);
            if (ModelState.IsValid)
            {
                //get payment info
                var paymentInfo = paymentController.GetPaymentInfo(form);
                //session save
                _httpContext.Session["OrderPaymentInfo"] = paymentInfo;
                return RedirectToRoute("CheckoutConfirm");
            }

            //If we got this far, something failed, redisplay form
            //model
            var model = PreparePaymentInfoModel(paymentMethod);
            return View(model);
        }
        

        public ActionResult Confirm()
        {
            //validation
            var cart = _workContext.CurrentCustomer.ShoppingCartItems
                .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                .LimitPerStore(_storeContext.CurrentStore.Id)
                .ToList();
            if (cart.Count == 0)
                return RedirectToRoute("ShoppingCart");

            if (_orderSettings.OnePageCheckoutEnabled)
                return RedirectToRoute("CheckoutOnePage");

            if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                return new HttpUnauthorizedResult();

            //model
            var model = PrepareConfirmOrderModel(cart);
            return View(model);
        }
        [HttpPost, ActionName("Confirm")]
        [ValidateInput(false)]
        public ActionResult ConfirmOrder()
        {
            //validation
            var cart = _workContext.CurrentCustomer.ShoppingCartItems
                .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                .LimitPerStore(_storeContext.CurrentStore.Id)
                .ToList();
            if (cart.Count == 0)
                return RedirectToRoute("ShoppingCart");

            if (_orderSettings.OnePageCheckoutEnabled)
                return RedirectToRoute("CheckoutOnePage");

            if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                return new HttpUnauthorizedResult();


            //model
            var model = PrepareConfirmOrderModel(cart);
            try
            {
                var processPaymentRequest = _httpContext.Session["OrderPaymentInfo"] as ProcessPaymentRequest;
                if (processPaymentRequest == null)
                {
                    //Check whether payment workflow is required
                    if (IsPaymentWorkflowRequired(cart))
                        return RedirectToRoute("CheckoutPaymentInfo");
                    else
                        processPaymentRequest = new ProcessPaymentRequest();
                }
                
                //prevent 2 orders being placed within an X seconds time frame
                if (!IsMinimumOrderPlacementIntervalValid(_workContext.CurrentCustomer))
                    throw new Exception(_localizationService.GetResource("Checkout.MinOrderPlacementInterval"));

                //place order
                processPaymentRequest.StoreId = _storeContext.CurrentStore.Id;
                processPaymentRequest.CustomerId = _workContext.CurrentCustomer.Id;
                processPaymentRequest.PaymentMethodSystemName = _workContext.CurrentCustomer.GetAttribute<string>(
                    SystemCustomerAttributeNames.SelectedPaymentMethod,
                    _genericAttributeService, _storeContext.CurrentStore.Id);
                var placeOrderResult = _orderProcessingService.PlaceOrder(processPaymentRequest);
                if (placeOrderResult.Success)
                {
                    _httpContext.Session["OrderPaymentInfo"] = null;
                    var postProcessPaymentRequest = new PostProcessPaymentRequest()
                    {
                        Order = placeOrderResult.PlacedOrder
                    };
                    _paymentService.PostProcessPayment(postProcessPaymentRequest);

                    if (_webHelper.IsRequestBeingRedirected || _webHelper.IsPostBeingDone)
                    {
                        //redirection or POST has been done in PostProcessPayment
                        return Content("Redirected");
                    }
                    else
                    {
                        return RedirectToRoute("CheckoutCompleted", new { orderId = placeOrderResult.PlacedOrder.Id });
                    }
                }
                else
                {
                    foreach (var error in placeOrderResult.Errors)
                        model.Warnings.Add(error);
                }
            }
            catch (Exception exc)
            {
                _logger.Warning(exc.Message, exc);
                model.Warnings.Add(exc.Message);
            }

            //If we got this far, something failed, redisplay form
            return View(model);
        }


        [ChildActionOnly]
        public ActionResult CheckoutProgress(CheckoutProgressStep step)
        {
            var model = new CheckoutProgressModel() {CheckoutProgressStep = step};
            return PartialView(model);
        }

        #endregion

        #region Methods (one page checkout)

        [NonAction]
        protected JsonResult OpcLoadStepAfterShippingMethod(List<ShoppingCartItem> cart)
        {
            //Check whether payment workflow is required
            //we ignore reward points during cart total calculation
            bool isPaymentWorkflowRequired = IsPaymentWorkflowRequired(cart, true);
            if (isPaymentWorkflowRequired)
            {
                //payment is required
                var paymentMethodModel = PreparePaymentMethodModel(cart);

                if (_paymentSettings.BypassPaymentMethodSelectionIfOnlyOne &&
                    paymentMethodModel.PaymentMethods.Count == 1 && !paymentMethodModel.DisplayRewardPoints)
                {
                    //if we have only one payment method and reward points are disabled or the current customer doesn't have any reward points
                    //so customer doesn't have to choose a payment method

                    var selectedPaymentMethodSystemName = paymentMethodModel.PaymentMethods[0].PaymentMethodSystemName;
                    _genericAttributeService.SaveAttribute<string>(_workContext.CurrentCustomer,
                        SystemCustomerAttributeNames.SelectedPaymentMethod,
                        selectedPaymentMethodSystemName, _storeContext.CurrentStore.Id);

                    var paymentMethodInst = _paymentService.LoadPaymentMethodBySystemName(selectedPaymentMethodSystemName);
                    if (paymentMethodInst == null ||
                        !paymentMethodInst.IsPaymentMethodActive(_paymentSettings) ||
                        !_pluginFinder.AuthenticateStore(paymentMethodInst.PluginDescriptor, _storeContext.CurrentStore.Id))
                        throw new Exception("Selected payment method can't be parsed");

                    return OpcLoadStepAfterPaymentMethod(paymentMethodInst, cart);
                }
                else
                {
                    //customer have to choose a payment method
                    return Json(new
                    {
                        update_section = new UpdateSectionJsonModel()
                        {
                            name = "payment-method",
                            html = this.RenderPartialViewToString("OpcPaymentMethods", paymentMethodModel)
                        },
                        goto_section = "payment_method"
                    });
                }
            }
            else
            {
                //payment is not required
                _genericAttributeService.SaveAttribute<string>(_workContext.CurrentCustomer,
                    SystemCustomerAttributeNames.SelectedPaymentMethod, null, _storeContext.CurrentStore.Id);

                var confirmOrderModel = PrepareConfirmOrderModel(cart);
                return Json(new
                {
                    update_section = new UpdateSectionJsonModel()
                    {
                        name = "confirm-order",
                        html = this.RenderPartialViewToString("OpcConfirmOrder", confirmOrderModel)
                    },
                    goto_section = "confirm_order"
                });
            }
        }

        [NonAction]
        protected JsonResult OpcLoadStepAfterPaymentMethod(IPaymentMethod paymentMethod, List<ShoppingCartItem> cart)
        {
            if (paymentMethod.SkipPaymentInfo)
            {
                //skip payment info page
                var paymentInfo = new ProcessPaymentRequest();
                //session save
                _httpContext.Session["OrderPaymentInfo"] = paymentInfo;

                var confirmOrderModel = PrepareConfirmOrderModel(cart);
                return Json(new
                {
                    update_section = new UpdateSectionJsonModel()
                    {
                        name = "confirm-order",
                        html = this.RenderPartialViewToString("OpcConfirmOrder", confirmOrderModel)
                    },
                    goto_section = "confirm_order"
                });
            }
            else
            {
                //return payment info page
                var paymenInfoModel = PreparePaymentInfoModel(paymentMethod);
                return Json(new
                {
                    update_section = new UpdateSectionJsonModel()
                    {
                        name = "payment-info",
                        html = this.RenderPartialViewToString("OpcPaymentInfo", paymenInfoModel)
                    },
                    goto_section = "payment_info"
                });
            }
        }

        public ActionResult OnePageCheckout()
        {
            //validation
            var cart = _workContext.CurrentCustomer.ShoppingCartItems
                .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                .LimitPerStore(_storeContext.CurrentStore.Id)
                .ToList();
            if (cart.Count == 0)
                return RedirectToRoute("ShoppingCart");

            if (!_orderSettings.OnePageCheckoutEnabled)
                return RedirectToRoute("Checkout");

            if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                return new HttpUnauthorizedResult();

            var model = new OnePageCheckoutModel()
            {
                ShippingRequired = cart.RequiresShipping(),
                DisableBillingAddressCheckoutStep = _orderSettings.DisableBillingAddressCheckoutStep
            };
            return View(model);
        }

        [ChildActionOnly]
        public ActionResult OpcBillingForm()
        {
            var billingAddressModel = PrepareBillingAddressModel(prePopulateNewAddressWithCustomerFields: true);
            return PartialView("OpcBillingAddress", billingAddressModel);
        }
 
        [ChildActionOnly]
        public ActionResult OpcShippingForm()
        {
            Session["cAddress"] = 0;
            var shippingAddressModel = PrepareShippingAddressModel(prePopulateNewAddressWithCustomerFields: true);
            return PartialView("OpcShippingAddress", shippingAddressModel);

        }

        public ActionResult saveShippingInfo(string shippingoption)
        {
            try
            {
                //validation
                var cart = _workContext.CurrentCustomer.ShoppingCartItems
                    .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                    .LimitPerStore(_storeContext.CurrentStore.Id)
                    .ToList();
                if (cart.Count == 0)
                    throw new Exception("Your cart is empty");

                if (!_orderSettings.OnePageCheckoutEnabled)
                    throw new Exception("One page checkout is disabled");

                if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                    throw new Exception("Anonymous checkout is not allowed");

                if (!cart.RequiresShipping())
                    throw new Exception("Shipping is not required");

                //parse selected method 
                //string shippingoption = form["shippingoption"];
                if (String.IsNullOrEmpty(shippingoption))
                    throw new Exception("Selected shipping method can't be parsed");
                var splittedOption = shippingoption.Split(new string[] { "___" }, StringSplitOptions.RemoveEmptyEntries);
                if (splittedOption.Length != 2)
                    throw new Exception("Selected shipping method can't be parsed");
                string selectedName = splittedOption[0];
                string shippingRateComputationMethodSystemName = splittedOption[1];

                //find it
                //performance optimization. try cache first
                var shippingOptions = _workContext.CurrentCustomer.GetAttribute<List<ShippingOption>>(SystemCustomerAttributeNames.OfferedShippingOptions, _storeContext.CurrentStore.Id);
                if (shippingOptions == null || shippingOptions.Count == 0)
                {
                    //not found? let's load them using shipping service
                    shippingOptions = _shippingService
                        .GetShippingOptions(cart, _workContext.CurrentCustomer.ShippingAddress, shippingRateComputationMethodSystemName, _storeContext.CurrentStore.Id)
                        .ShippingOptions
                        .ToList();
                }
                else
                {
                    //loaded cached results. let's filter result by a chosen shipping rate computation method
                    shippingOptions = shippingOptions.Where(so => so.ShippingRateComputationMethodSystemName.Equals(shippingRateComputationMethodSystemName, StringComparison.InvariantCultureIgnoreCase))
                        .ToList();
                }

                var shippingOption = shippingOptions
                    .Find(so => !String.IsNullOrEmpty(so.Name) && so.Name.Equals(selectedName, StringComparison.InvariantCultureIgnoreCase));
                if (shippingOption == null)
                    throw new Exception("Selected shipping method can't be loaded");

                //save
                _genericAttributeService.SaveAttribute(_workContext.CurrentCustomer, SystemCustomerAttributeNames.SelectedShippingOption, shippingOption, _storeContext.CurrentStore.Id);

                //load next step
                return Json(new { success = "true" });

            }
            catch (Exception ex)
            {

            }

            return Json(new { success = "true" });
        }
        public OrderTotalsModel GetOrderTotalModel(List<ShoppingCartItem> cart)
        {
            var model = new Nop.Web.Models.ShoppingCart.OrderTotalsModel();
            model.IsEditable = false;

           
                //subtotal
                decimal subtotalBase = decimal.Zero;
                decimal orderSubTotalDiscountAmountBase = decimal.Zero;
                Discount orderSubTotalAppliedDiscount = null;
                decimal subTotalWithoutDiscountBase = decimal.Zero;
                decimal subTotalWithDiscountBase = decimal.Zero;
                var subTotalIncludingTax = _workContext.TaxDisplayType == Nop.Core.Domain.Tax.TaxDisplayType.IncludingTax && false;
                _orderTotalCalculationService.GetShoppingCartSubTotal(cart, subTotalIncludingTax,
                    out orderSubTotalDiscountAmountBase, out orderSubTotalAppliedDiscount,
                    out subTotalWithoutDiscountBase, out subTotalWithDiscountBase);
                subtotalBase = subTotalWithoutDiscountBase;
                decimal subtotal = _currencyService.ConvertFromPrimaryStoreCurrency(subtotalBase, _workContext.WorkingCurrency);
                model.SubTotal = _priceFormatter.FormatPrice(subtotal, true, _workContext.WorkingCurrency, _workContext.WorkingLanguage, subTotalIncludingTax);

                if (orderSubTotalDiscountAmountBase > decimal.Zero)
                {
                    decimal orderSubTotalDiscountAmount = _currencyService.ConvertFromPrimaryStoreCurrency(orderSubTotalDiscountAmountBase, _workContext.WorkingCurrency);
                    model.SubTotalDiscount = _priceFormatter.FormatPrice(-orderSubTotalDiscountAmount, true, _workContext.WorkingCurrency, _workContext.WorkingLanguage, subTotalIncludingTax);
                    model.AllowRemovingSubTotalDiscount = orderSubTotalAppliedDiscount != null &&
                                                          orderSubTotalAppliedDiscount.RequiresCouponCode &&
                                                          !String.IsNullOrEmpty(orderSubTotalAppliedDiscount.CouponCode) &&
                                                          model.IsEditable;
                }


                //shipping info
                model.RequiresShipping = cart.RequiresShipping();
                if (model.RequiresShipping)
                {
                    decimal? shoppingCartShippingBase = _orderTotalCalculationService.GetShoppingCartShippingTotal(cart);
                    if (shoppingCartShippingBase.HasValue)
                    {
                        decimal shoppingCartShipping = _currencyService.ConvertFromPrimaryStoreCurrency(shoppingCartShippingBase.Value, _workContext.WorkingCurrency);
                        model.Shipping = _priceFormatter.FormatShippingPrice(shoppingCartShipping, true);

                        //selected shipping method
                        var shippingOption = _workContext.CurrentCustomer.GetAttribute<ShippingOption>(SystemCustomerAttributeNames.SelectedShippingOption, _storeContext.CurrentStore.Id);
                        if (shippingOption != null)
                            model.SelectedShippingMethod = shippingOption.Name;
                    }
                }

                //payment method fee
                string paymentMethodSystemName = _workContext.CurrentCustomer.GetAttribute<string>(
                    SystemCustomerAttributeNames.SelectedPaymentMethod, _storeContext.CurrentStore.Id);
                decimal paymentMethodAdditionalFee = _paymentService.GetAdditionalHandlingFee(cart, paymentMethodSystemName);
                decimal paymentMethodAdditionalFeeWithTaxBase = _taxService.GetPaymentMethodAdditionalFee(paymentMethodAdditionalFee, _workContext.CurrentCustomer);
                if (paymentMethodAdditionalFeeWithTaxBase > decimal.Zero)
                {
                    decimal paymentMethodAdditionalFeeWithTax = _currencyService.ConvertFromPrimaryStoreCurrency(paymentMethodAdditionalFeeWithTaxBase, _workContext.WorkingCurrency);
                    model.PaymentMethodAdditionalFee = _priceFormatter.FormatPaymentMethodAdditionalFee(paymentMethodAdditionalFeeWithTax, true);
                }

                //tax
                bool displayTax = true;
                bool displayTaxRates = true;
             
                    SortedDictionary<decimal, decimal> taxRates = null;
                    decimal shoppingCartTaxBase = _orderTotalCalculationService.GetTaxTotal(cart, out taxRates);
                    decimal shoppingCartTax = _currencyService.ConvertFromPrimaryStoreCurrency(shoppingCartTaxBase, _workContext.WorkingCurrency);

                    if (shoppingCartTaxBase == 0 )
                    {
                        displayTax = false;
                        displayTaxRates = false;
                    }
                    else
                    {
                        displayTaxRates =  taxRates.Count > 0;
                        displayTax = !displayTaxRates;

                        model.Tax = _priceFormatter.FormatPrice(shoppingCartTax, true, false);
                        foreach (var tr in taxRates)
                        {
                            model.TaxRates.Add(new Nop.Web.Models.ShoppingCart.OrderTotalsModel.TaxRate()
                            {
                                Rate = _priceFormatter.FormatTaxRate(tr.Key),
                                Value = _priceFormatter.FormatPrice(_currencyService.ConvertFromPrimaryStoreCurrency(tr.Value, _workContext.WorkingCurrency), true, false),
                            });
                        }
                    }
                
                model.DisplayTaxRates = displayTaxRates;
                model.DisplayTax = displayTax;

                //total
                decimal orderTotalDiscountAmountBase = decimal.Zero;
                Discount orderTotalAppliedDiscount = null;
                List<AppliedGiftCard> appliedGiftCards = null;
                int redeemedRewardPoints = 0;
                decimal redeemedRewardPointsAmount = decimal.Zero;
                decimal? shoppingCartTotalBase = _orderTotalCalculationService.GetShoppingCartTotal(cart,
                    out orderTotalDiscountAmountBase, out orderTotalAppliedDiscount,
                    out appliedGiftCards, out redeemedRewardPoints, out redeemedRewardPointsAmount);
                if (shoppingCartTotalBase.HasValue)
                {
                    decimal shoppingCartTotal = _currencyService.ConvertFromPrimaryStoreCurrency(shoppingCartTotalBase.Value, _workContext.WorkingCurrency);
                    model.OrderTotal = _priceFormatter.FormatPrice(shoppingCartTotal, true, false);
                }

                //discount
                if (orderTotalDiscountAmountBase > decimal.Zero)
                {
                    decimal orderTotalDiscountAmount = _currencyService.ConvertFromPrimaryStoreCurrency(orderTotalDiscountAmountBase, _workContext.WorkingCurrency);
                    model.OrderTotalDiscount = _priceFormatter.FormatPrice(-orderTotalDiscountAmount, true, false);
                    model.AllowRemovingOrderTotalDiscount = orderTotalAppliedDiscount != null &&
                        orderTotalAppliedDiscount.RequiresCouponCode &&
                        !String.IsNullOrEmpty(orderTotalAppliedDiscount.CouponCode) &&
                        model.IsEditable;
                }

                //gift cards
                if (appliedGiftCards != null && appliedGiftCards.Count > 0)
                {
                    foreach (var appliedGiftCard in appliedGiftCards)
                    {
                        var gcModel = new Nop.Web.Models.ShoppingCart.OrderTotalsModel.GiftCard()
                        {
                            Id = appliedGiftCard.GiftCard.Id,
                            CouponCode = appliedGiftCard.GiftCard.GiftCardCouponCode,
                        };
                        decimal amountCanBeUsed = _currencyService.ConvertFromPrimaryStoreCurrency(appliedGiftCard.AmountCanBeUsed, _workContext.WorkingCurrency);
                        gcModel.Amount = _priceFormatter.FormatPrice(-amountCanBeUsed, true, false);

                        decimal remainingAmountBase = appliedGiftCard.GiftCard.GetGiftCardRemainingAmount() - appliedGiftCard.AmountCanBeUsed;
                        decimal remainingAmount = _currencyService.ConvertFromPrimaryStoreCurrency(remainingAmountBase, _workContext.WorkingCurrency);
                        gcModel.Remaining = _priceFormatter.FormatPrice(remainingAmount, true, false);

                        model.GiftCards.Add(gcModel);
                    }
                }

                //reward points
                if (redeemedRewardPointsAmount > decimal.Zero)
                {
                    decimal redeemedRewardPointsAmountInCustomerCurrency = _currencyService.ConvertFromPrimaryStoreCurrency(redeemedRewardPointsAmount, _workContext.WorkingCurrency);
                    model.RedeemedRewardPoints = redeemedRewardPoints;
                    model.RedeemedRewardPointsAmount = _priceFormatter.FormatPrice(-redeemedRewardPointsAmountInCustomerCurrency, true, false);
                }
                return model;
            

        }
      //  [ValidateInput(false)]
      //  public ActionResult PlaceOrder(string place)
      //  {

      //      try
      //      {
      //          //validation
      //          var cart = _workContext.CurrentCustomer.ShoppingCartItems
      //              .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
      //              .LimitPerStore(_storeContext.CurrentStore.Id)
      //              .ToList();
      //          if (cart.Count == 0)
      //              throw new Exception("Your cart is empty");

      //          if (!_orderSettings.OnePageCheckoutEnabled)
      //              throw new Exception("One page checkout is disabled");

      //          if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
      //              throw new Exception("Anonymous checkout is not allowed");

      //          //prevent 2 orders being placed within an X seconds time frame
      //          if (!IsMinimumOrderPlacementIntervalValid(_workContext.CurrentCustomer))
      //              throw new Exception(_localizationService.GetResource("Checkout.MinOrderPlacementInterval"));

      //          //place order
      //          var processPaymentRequest = _httpContext.Session["OrderPaymentInfo"] as ProcessPaymentRequest;
      //          Dictionary<string, string> sdkConfig = new Dictionary<string, string>();
      //          sdkConfig.Add("endpoint", "https://api.sandbox.paypal.com");
      //          sdkConfig.Add("mode", "sandbox");
      //          OAuthTokenCredential tokenCredential =
      //new OAuthTokenCredential("Ad_GBRD0-iRQbHXvI6IQnWBxUr-KwPLYfwYs_m9h849hvDuEMaeKz5fZQ7WC", "EI-TLBBBlxeyGvkxixY6a0s5kEcGgOI00MYYqDf13DVGAoJ1xNZp_G42iHBc", sdkConfig);

      //          string accessToken = tokenCredential.GetAccessToken();

      //          var billingAddress = new PayPal.Api.Payments.Address();
      //          billingAddress.line1 = _workContext.CurrentCustomer.BillingAddress.Address1;
      //          billingAddress.city = _workContext.CurrentCustomer.BillingAddress.City;
      //          billingAddress.country_code = _workContext.CurrentCustomer.BillingAddress.Country.TwoLetterIsoCode;
      //          billingAddress.postal_code = _workContext.CurrentCustomer.BillingAddress.ZipPostalCode;
      //          billingAddress.state = _workContext.CurrentCustomer.BillingAddress.StateProvince.Abbreviation;

      //          CreditCard creditCard = new CreditCard();
      //          creditCard.number = processPaymentRequest.CreditCardNumber;
      //          creditCard.type = processPaymentRequest.CreditCardType;
      //          creditCard.expire_month = processPaymentRequest.CreditCardExpireMonth;
      //          creditCard.expire_year = processPaymentRequest.CreditCardExpireYear;
      //          creditCard.cvv2 = int.Parse(processPaymentRequest.CreditCardCvv2);
      //          creditCard.first_name = _workContext.CurrentCustomer.BillingAddress.FirstName;
      //          creditCard.last_name = _workContext.CurrentCustomer.BillingAddress.LastName;
      //          creditCard.billing_address = billingAddress;

      //          OrderTotalsModel sm = GetOrderTotalModel(cart);
      //          Details amountDetails = new Details();
      //          amountDetails.subtotal = sm.SubTotal;
      //          amountDetails.tax = sm.Tax;
      //          amountDetails.shipping = sm.Shipping;

      //          Amount amount = new Amount();
      //          amount.total = sm.OrderTotal;
      //          amount.currency = "USD";
      //          amount.details = amountDetails;

      //          Transaction transaction = new Transaction();
      //          transaction.amount = amount;
      //          transaction.description = "This is the payment transaction description.";

      //          List<Transaction> transactions = new List<Transaction>();
      //          transactions.Add(transaction);

      //          FundingInstrument fundingInstrument = new FundingInstrument();
      //          fundingInstrument.credit_card = creditCard;

      //          List<FundingInstrument> fundingInstruments = new List<FundingInstrument>();
      //          fundingInstruments.Add(fundingInstrument);

      //          Payer payer = new Payer();
      //          payer.funding_instruments = fundingInstruments;
      //          payer.payment_method = "credit_card";

      //          Payment payment = new Payment();
      //          payment.intent = "sale";
      //          payment.payer = payer;
      //          payment.transactions = transactions;

      //          Payment createdPayment = payment.Create(accessToken);
      //          var outp = createdPayment;

      //          PlaceOrderResult placeOrderResult = new PlaceOrderResult();
      //          if (createdPayment.state.ToLower() == "approved")
      //          {

      //              //save order in data storage
      //              //uncomment this line to support transactions
      //              //using (var scope = new System.Transactions.TransactionScope())
      //              {
      //                  #region Save order details

      //                  var shippingStatus = ShippingStatus.NotYetShipped;
                        
      //                  Nop.Core.Domain.Orders.Order initialOrder = _orderService.GetOrderById(processPaymentRequest.InitialOrderId);
      //                  var shippingOption = _workContext.CurrentCustomer.GetAttribute<ShippingOption>(SystemCustomerAttributeNames.SelectedShippingOption, processPaymentRequest.StoreId);
                      
      //                  var order = new Nop.Core.Domain.Orders.Order()
      //                  {
      //                      StoreId = processPaymentRequest.StoreId,
      //                      OrderGuid = processPaymentRequest.OrderGuid,
      //                      CustomerId = processPaymentRequest.CustomerId,
      //                      CustomerLanguageId = initialOrder.CustomerLanguageId,
      //                      CustomerTaxDisplayType = initialOrder.CustomerTaxDisplayType,
      //                      CustomerIp = _webHelper.GetCurrentIpAddress(),
      //                      OrderSubtotalInclTax = Decimal.Parse(sm.SubTotal) + ((Decimal)0.07 *Decimal.Parse(sm.SubTotal)),
      //                      OrderSubtotalExclTax = Decimal.Parse(sm.SubTotal),
      //                      OrderSubTotalDiscountInclTax = Decimal.Parse(sm.OrderTotal),
      //                      OrderSubTotalDiscountExclTax = Decimal.Parse(sm.SubTotal),
      //                      OrderShippingInclTax = Decimal.Parse(sm.Shipping),
      //                      OrderShippingExclTax = Decimal.Parse(sm.Shipping),
      //                      PaymentMethodAdditionalFeeInclTax = 0,
      //                      PaymentMethodAdditionalFeeExclTax = 0,
      //                      TaxRates = "Sales:7%",
      //                      OrderTax = Decimal.Parse(sm.Tax),
      //                      OrderTotal = Decimal.Parse(sm.OrderTotal),
      //                      RefundedAmount = decimal.Zero,
      //                      OrderDiscount = decimal.Zero,
      //                      CheckoutAttributeDescription = initialOrder.CheckoutAttributeDescription,
      //                      CheckoutAttributesXml = initialOrder.CheckoutAttributesXml,
      //                      CustomerCurrencyCode = initialOrder.CustomerCurrencyCode,
      //                      CurrencyRate = initialOrder.CurrencyRate,
      //                      AffiliateId = 0,
      //                      OrderStatus = OrderStatus.Pending,
      //                      AllowStoringCreditCardNumber = true,
      //                      CardType =  OrderProcessingService._encryptionService2.EncryptText(processPaymentRequest.CreditCardType),
      //                      CardName = OrderProcessingService._encryptionService2.EncryptText(processPaymentRequest.CreditCardName) ,
      //                      CardNumber = OrderProcessingService._encryptionService2.EncryptText(processPaymentRequest.CreditCardNumber),
      //                      MaskedCreditCardNumber = OrderProcessingService._encryptionService2.EncryptText(_paymentService.GetMaskedCreditCardNumber(processPaymentRequest.CreditCardNumber)),
      //                      CardCvv2 =  OrderProcessingService._encryptionService2.EncryptText(processPaymentRequest.CreditCardCvv2) ,
      //                      CardExpirationMonth =OrderProcessingService._encryptionService2.EncryptText(processPaymentRequest.CreditCardExpireMonth.ToString()) ,
      //                      CardExpirationYear = OrderProcessingService._encryptionService2.EncryptText(processPaymentRequest.CreditCardExpireYear.ToString()) ,
      //                      PaymentMethodSystemName = processPaymentRequest.PaymentMethodSystemName,
      //                      AuthorizationTransactionId = createdPayment.id,
      //                      AuthorizationTransactionCode = createdPayment.intent,
      //                      AuthorizationTransactionResult = createdPayment.state,
      //                      CaptureTransactionId = string.Empty,
      //                      CaptureTransactionResult = string.Empty,
      //                      SubscriptionTransactionId = string.Empty,
      //                      PurchaseOrderNumber = processPaymentRequest.PurchaseOrderNumber,
      //                      PaymentStatus = Nop.Core.Domain.Payments.PaymentStatus.Paid,
      //                      PaidDateUtc = null,
      //                      BillingAddress = _workContext.CurrentCustomer.BillingAddress,
      //                      ShippingAddress = _workContext.CurrentCustomer.ShippingAddress,
      //                      ShippingStatus = shippingStatus,
      //                      ShippingMethod = shippingOption.Name,
      //                      PickUpInStore = false,
      //                      ShippingRateComputationMethodSystemName = shippingOption.ShippingRateComputationMethodSystemName,
      //                      CustomValuesXml = processPaymentRequest.SerializeCustomValues(),
      //                      VatNumber = initialOrder.VatNumber,
      //                      CreatedOnUtc = DateTime.UtcNow
      //                  };

      //                  _orderService.InsertOrder(order);

      //             //     result.PlacedOrder = order;

      //                  if (!processPaymentRequest.IsRecurringPayment)
      //                  {
      //                      //move shopping cart items to order items
      //                      foreach (var sc in cart)
      //                      {
      //                          //prices
      //                          decimal taxRate = decimal.Zero;
      //                          decimal scUnitPrice = OrderProcessingService._priceCalculationService2.GetUnitPrice(sc, true);
      //                          decimal scSubTotal = OrderProcessingService._priceCalculationService2.GetSubTotal(sc, true);
      //                          decimal scUnitPriceInclTax = _taxService.GetProductPrice(sc.Product, scUnitPrice, true, _workContext.CurrentCustomer, out taxRate);
      //                          decimal scUnitPriceExclTax = _taxService.GetProductPrice(sc.Product, scUnitPrice, false, _workContext.CurrentCustomer, out taxRate);
      //                          decimal scSubTotalInclTax = _taxService.GetProductPrice(sc.Product, scSubTotal, true, _workContext.CurrentCustomer, out taxRate);
      //                          decimal scSubTotalExclTax = _taxService.GetProductPrice(sc.Product, scSubTotal, false, _workContext.CurrentCustomer, out taxRate);

      //                          //discounts
      //                          Discount scDiscount = null;
      //                          //decimal discountAmount = _priceCalculationService.GetDiscountAmount(sc, out scDiscount);
      //                          //decimal discountAmountInclTax = _taxService.GetProductPrice(sc.Product, discountAmount, true, _workContext.CurrentCustomer, out taxRate);
      //                          //decimal discountAmountExclTax = _taxService.GetProductPrice(sc.Product, discountAmount, false, _workContext.CurrentCustomer, out taxRate);
      //                          //if (scDiscount != null && !appliedDiscounts.ContainsDiscount(scDiscount))
      //                          //    appliedDiscounts.Add(scDiscount);

      //                          //attributes
      //                          string attributeDescription = OrderProcessingService._productAttributeFormatter2.FormatAttributes(sc.Product, sc.AttributesXml, _workContext.CurrentCustomer);

      //                          var itemWeight = _shippingService.GetShoppingCartItemWeight(sc);

      //                          //save order item
      //                          var orderItem = new OrderItem()
      //                          {
      //                              OrderItemGuid = Guid.NewGuid(),
      //                              Order = order,
      //                              ProductId = sc.ProductId,
      //                              UnitPriceInclTax = scUnitPriceInclTax,
      //                              UnitPriceExclTax = scUnitPriceExclTax,
      //                              PriceInclTax = scSubTotalInclTax,
      //                              PriceExclTax = scSubTotalExclTax,
      //                              OriginalProductCost = OrderProcessingService._priceCalculationService2.GetProductCost(sc.Product, sc.AttributesXml),
      //                              AttributeDescription = attributeDescription,
      //                              AttributesXml = sc.AttributesXml,
      //                              Quantity = sc.Quantity,
      //                              DiscountAmountInclTax = scSubTotalInclTax,
      //                              DiscountAmountExclTax = scSubTotalExclTax,
      //                              DownloadCount = 0,
      //                              IsDownloadActivated = false,
      //                              LicenseDownloadId = 0,
      //                              ItemWeight = itemWeight,
      //                          };
      //                          order.OrderItems.Add(orderItem);
      //                          _orderService.UpdateOrder(order);

      //                          //gift cards
      //                          //if (sc.Product.IsGiftCard)
      //                          //{
      //                          //    string giftCardRecipientName, giftCardRecipientEmail,
      //                          //        giftCardSenderName, giftCardSenderEmail, giftCardMessage;
      //                          //    _productAttributeParser.GetGiftCardAttribute(sc.AttributesXml,
      //                          //        out giftCardRecipientName, out giftCardRecipientEmail,
      //                          //        out giftCardSenderName, out giftCardSenderEmail, out giftCardMessage);

      //                          //    for (int i = 0; i < sc.Quantity; i++)
      //                          //    {
      //                          //        var gc = new GiftCard()
      //                          //        {
      //                          //            GiftCardType = sc.Product.GiftCardType,
      //                          //            PurchasedWithOrderItem = orderItem,
      //                          //            Amount = scUnitPriceExclTax,
      //                          //            IsGiftCardActivated = false,
      //                          //            GiftCardCouponCode = _giftCardService.GenerateGiftCardCode(),
      //                          //            RecipientName = giftCardRecipientName,
      //                          //            RecipientEmail = giftCardRecipientEmail,
      //                          //            SenderName = giftCardSenderName,
      //                          //            SenderEmail = giftCardSenderEmail,
      //                          //            Message = giftCardMessage,
      //                          //            IsRecipientNotified = false,
      //                          //            CreatedOnUtc = DateTime.UtcNow
      //                          //        };
      //                          //        _giftCardService.InsertGiftCard(gc);
      //                          //    }
      //                          //}

      //                          //inventory
      //                          OrderProcessingService._productService2.AdjustInventory(sc.Product, true, sc.Quantity, sc.AttributesXml);
      //                      }

      //                      //clear shopping cart
      //                      cart.ToList().ForEach(sci => _shoppingCartService.DeleteShoppingCartItem(sci, false));
      //                  }
                       
                        

      //                  ////discount usage history
      //                  //if (!processPaymentRequest.IsRecurringPayment)
      //                  //    foreach (var discount in appliedDiscounts)
      //                  //    {
      //                  //        var duh = new DiscountUsageHistory()
      //                  //        {
      //                  //            Discount = discount,
      //                  //            Order = order,
      //                  //            CreatedOnUtc = DateTime.UtcNow
      //                  //        };
      //                  //        _discountService.InsertDiscountUsageHistory(duh);
      //                  //    }

      //                  ////gift card usage history
      //                  //if (!processPaymentRequest.IsRecurringPayment)
      //                  //    if (appliedGiftCards != null)
      //                  //        foreach (var agc in appliedGiftCards)
      //                  //        {
      //                  //            decimal amountUsed = agc.AmountCanBeUsed;
      //                  //            var gcuh = new GiftCardUsageHistory()
      //                  //            {
      //                  //                GiftCard = agc.GiftCard,
      //                  //                UsedWithOrder = order,
      //                  //                UsedValue = amountUsed,
      //                  //                CreatedOnUtc = DateTime.UtcNow
      //                  //            };
      //                  //            agc.GiftCard.GiftCardUsageHistory.Add(gcuh);
      //                  //            _giftCardService.UpdateGiftCard(agc.GiftCard);
      //                  //        }

      //                  ////reward points history
      //                  //if (redeemedRewardPointsAmount > decimal.Zero)
      //                  //{
      //                  //    customer.AddRewardPointsHistoryEntry(-redeemedRewardPoints,
      //                  //        string.Format(_localizationService.GetResource("RewardPoints.Message.RedeemedForOrder", order.CustomerLanguageId), order.Id),
      //                  //        order,
      //                  //        redeemedRewardPointsAmount);
      //                  //    _customerService.UpdateCustomer(customer);
      //                  //}

      //                  ////recurring orders
      //                  //if (!processPaymentRequest.IsRecurringPayment && isRecurringShoppingCart)
      //                  //{
      //                  //    //create recurring payment (the first payment)
      //                  //    var rp = new RecurringPayment()
      //                  //    {
      //                  //        CycleLength = processPaymentRequest.RecurringCycleLength,
      //                  //        CyclePeriod = processPaymentRequest.RecurringCyclePeriod,
      //                  //        TotalCycles = processPaymentRequest.RecurringTotalCycles,
      //                  //        StartDateUtc = DateTime.UtcNow,
      //                  //        IsActive = true,
      //                  //        CreatedOnUtc = DateTime.UtcNow,
      //                  //        InitialOrder = order,
      //                  //    };
      //                  //    _orderService.InsertRecurringPayment(rp);


      //                  //    var recurringPaymentType = _paymentService.GetRecurringPaymentType(processPaymentRequest.PaymentMethodSystemName);
      //                  //    switch (recurringPaymentType)
      //                  //    {
      //                  //        case RecurringPaymentType.NotSupported:
      //                  //            {
      //                  //                //not supported
      //                  //            }
      //                  //            break;
      //                  //        case RecurringPaymentType.Manual:
      //                  //            {
      //                  //                //first payment
      //                  //                var rph = new RecurringPaymentHistory()
      //                  //                {
      //                  //                    RecurringPayment = rp,
      //                  //                    CreatedOnUtc = DateTime.UtcNow,
      //                  //                    OrderId = order.Id,
      //                  //                };
      //                  //                rp.RecurringPaymentHistory.Add(rph);
      //                  //                _orderService.UpdateRecurringPayment(rp);
      //                  //            }
      //                  //            break;
      //                  //        case RecurringPaymentType.Automatic:
      //                  //            {
      //                  //                //will be created later (process is automated)
      //                  //            }
      //                  //            break;
      //                  //        default:
      //                  //            break;
      //                  //    }
      //                  //}

      //                  #endregion

      //                  #region Notifications & notes

      //                  //notes, messages
      //                  if (_workContext.OriginalCustomerIfImpersonated != null)
      //                  {
      //                      //this order is placed by a store administrator impersonating a customer
      //                      order.OrderNotes.Add(new OrderNote()
      //                      {
      //                          Note = string.Format("Order placed by a store owner ('{0}'. ID = {1}) impersonating the customer.",
      //                              _workContext.OriginalCustomerIfImpersonated.Email, _workContext.OriginalCustomerIfImpersonated.Id),
      //                          DisplayToCustomer = false,
      //                          CreatedOnUtc = DateTime.UtcNow
      //                      });
      //                      _orderService.UpdateOrder(order);
      //                  }
      //                  else
      //                  {
      //                      order.OrderNotes.Add(new OrderNote()
      //                      {
      //                          Note = "Order placed",
      //                          DisplayToCustomer = false,
      //                          CreatedOnUtc = DateTime.UtcNow
      //                      });
      //                      _orderService.UpdateOrder(order);
      //                  }


      //                  //send email notifications
      //                  int orderPlacedStoreOwnerNotificationQueuedEmailId = OrderProcessingService._workflowMessageService2.SendOrderPlacedStoreOwnerNotification(order, OrderProcessingService._localizationSettings2.DefaultAdminLanguageId);
      //                  if (orderPlacedStoreOwnerNotificationQueuedEmailId > 0)
      //                  {
      //                      order.OrderNotes.Add(new OrderNote()
      //                      {
      //                          Note = string.Format("\"Order placed\" email (to store owner) has been queued. Queued email identifier: {0}.", orderPlacedStoreOwnerNotificationQueuedEmailId),
      //                          DisplayToCustomer = false,
      //                          CreatedOnUtc = DateTime.UtcNow
      //                      });
      //                      _orderService.UpdateOrder(order);
      //                  }

      //                  var orderPlacedAttachmentFilePath = _orderSettings.AttachPdfInvoiceToOrderPlacedEmail ?
      //                      _pdfService.PrintOrderToPdf(order, 0) : null;
      //                  var orderPlacedAttachmentFileName = _orderSettings.AttachPdfInvoiceToOrderPlacedEmail ?
      //                      "order.pdf" : null;
      //                  int orderPlacedCustomerNotificationQueuedEmailId = _workflowMessageService
      //                      .SendOrderPlacedCustomerNotification(order, order.CustomerLanguageId, orderPlacedAttachmentFilePath, orderPlacedAttachmentFileName);
      //                  if (orderPlacedCustomerNotificationQueuedEmailId > 0)
      //                  {
      //                      order.OrderNotes.Add(new OrderNote()
      //                      {
      //                          Note = string.Format("\"Order placed\" email (to customer) has been queued. Queued email identifier: {0}.", orderPlacedCustomerNotificationQueuedEmailId),
      //                          DisplayToCustomer = false,
      //                          CreatedOnUtc = DateTime.UtcNow
      //                      });
      //                      _orderService.UpdateOrder(order);
      //                  }

      //                  var vendors = new List<Vendor>();
      //                  foreach (var orderItem in order.OrderItems)
      //                  {
      //                      var vendorId = orderItem.Product.VendorId;
      //                      //find existing
      //                      var vendor = vendors.FirstOrDefault(v => v.Id == vendorId);
      //                      if (vendor == null)
      //                      {
      //                          //not found. load by Id
      //                          vendor = _vendorService.GetVendorById(vendorId);
      //                          if (vendor != null && !vendor.Deleted && vendor.Active)
      //                          {
      //                              vendors.Add(vendor);
      //                          }
      //                      }
      //                  }
      //                  foreach (var vendor in vendors)
      //                  {
      //                      int orderPlacedVendorNotificationQueuedEmailId = _workflowMessageService.SendOrderPlacedVendorNotification(order, vendor, order.CustomerLanguageId);
      //                      if (orderPlacedVendorNotificationQueuedEmailId > 0)
      //                      {
      //                          order.OrderNotes.Add(new OrderNote()
      //                          {
      //                              Note = string.Format("\"Order placed\" email (to vendor) has been queued. Queued email identifier: {0}.", orderPlacedVendorNotificationQueuedEmailId),
      //                              DisplayToCustomer = false,
      //                              CreatedOnUtc = DateTime.UtcNow
      //                          });
      //                          _orderService.UpdateOrder(order);
      //                      }
      //                  }

      //                  //check order status
      //                  CheckOrderStatus(order);

      //                  //reset checkout data
      //                  if (!processPaymentRequest.IsRecurringPayment)
      //                      _customerService.ResetCheckoutData(customer, processPaymentRequest.StoreId, clearCouponCodes: true, clearCheckoutAttributes: true);

      //                  if (!processPaymentRequest.IsRecurringPayment)
      //                  {
      //                      _customerActivityService.InsertActivity(
      //                          "PublicStore.PlaceOrder",
      //                          _localizationService.GetResource("ActivityLog.PublicStore.PlaceOrder"),
      //                          order.Id);
      //                  }

      //                  //uncomment this line to support transactions
      //                  //scope.Complete();

      //                  //raise event       
      //                  _eventPublisher.PublishOrderPlaced(order);

      //                  if (order.PaymentStatus == PaymentStatus.Paid)
      //                  {
      //                      ProcessOrderPaid(order);
      //                  }
      //                  #endregion
      //              }
      //          }
      //      }

      //      catch(Exception ex)
      //      {

      //      }

      //      //////


       
           
      //      return Json(new { success = "true" });
      //  }

        [ValidateInput(false)]
        public ActionResult SaveBillingInfo(string formcollection, string cardcollection)
        {      
            try
            {
                //var form = formcollection;
                System.Web.Script.Serialization.JavaScriptSerializer serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                FormCollection fc = new FormCollection();
                
                AddressModel amodel = (AddressModel)serializer.Deserialize(formcollection, typeof(AddressModel));
                MyPaymentModel cmodel = (MyPaymentModel)serializer.Deserialize(cardcollection, typeof(MyPaymentModel));

                amodel.FirstName = cmodel.CardholderName.Split(' ').First();
                amodel.LastName = cmodel.CardholderName.Split(' ').Last();
                fc.Add("CreditCardType", cmodel.CreditCardType);
                fc.Add("CardholderName", cmodel.CardholderName);
                fc.Add("CardNumber", cmodel.CardNumber);
                fc.Add("ExpireMonth", cmodel.ExpireMonth.ToString());
                fc.Add("ExpireYear", cmodel.ExpireYear.ToString());
                fc.Add("CardCode", cmodel.CardCode);
                //validation
                var cart = _workContext.CurrentCustomer.ShoppingCartItems
                    .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                    .LimitPerStore(_storeContext.CurrentStore.Id)
                    .ToList();
                if (cart.Count == 0)
                    throw new Exception("Your cart is empty");

                if (!_orderSettings.OnePageCheckoutEnabled)
                    throw new Exception("One page checkout is disabled");

                if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                    throw new Exception("Anonymous checkout is not allowed");

               // int billingAddressId = 0;
                //new address
                var model = new CheckoutBillingAddressModel();
                model.NewAddress = amodel;
                ////validate model
                //TryValidateModel(model.NewAddress);
                //if (!ModelState.IsValid)
                //{
                //    //model is not valid. redisplay the form with errors
                //    var billingAddressModel = PrepareBillingAddressModel(selectedCountryId: model.NewAddress.CountryId);
                //    billingAddressModel.NewAddressPreselected = true;
                //    return Json(new
                //    {
                //        update_section = new UpdateSectionJsonModel()
                //        {
                //            name = "billing",
                //            html = this.RenderPartialViewToString("OpcBillingAddress", billingAddressModel)
                //        },
                //        wrong_billing_address = true,
                //    });
                //}

                //try to find an address with the same values (don't duplicate records)
                var address = model.NewAddress.ToEntity();
                address.CreatedOnUtc = DateTime.UtcNow;
                //some validation
                if (address.CountryId == 0)
                    address.CountryId = null;
                if (address.StateProvinceId == 0)
                    address.StateProvinceId = null;
                if (address.CountryId.HasValue && address.CountryId.Value > 0)
                {
                    address.Country = _countryService.GetCountryById(address.CountryId.Value);
                }
                //  _workContext.CurrentCustomer.Addresses.Add(address);

                _workContext.CurrentCustomer.BillingAddress = address;
                //_customerService.UpdateCustomer(_workContext.CurrentCustomer);

                //Check whether payment workflow is required

                //payment is required
                var paymentMethodModel = PreparePaymentMethodModel(cart);

                //if we have only one payment method and reward points are disabled or the current customer doesn't have any reward points
                //so customer doesn't have to choose a payment method

                var selectedPaymentMethodSystemName = paymentMethodModel.PaymentMethods[0].PaymentMethodSystemName;
                _genericAttributeService.SaveAttribute<string>(_workContext.CurrentCustomer,
                    SystemCustomerAttributeNames.SelectedPaymentMethod,
                    selectedPaymentMethodSystemName, _storeContext.CurrentStore.Id);

                var paymentMethodInst = _paymentService.LoadPaymentMethodBySystemName(selectedPaymentMethodSystemName);
                if (paymentMethodInst == null ||
                    !paymentMethodInst.IsPaymentMethodActive(_paymentSettings) ||
                    !_pluginFinder.AuthenticateStore(paymentMethodInst.PluginDescriptor, _storeContext.CurrentStore.Id))
                    throw new Exception("Selected payment method can't be parsed");

                            var paymentControllerType = paymentMethodInst.GetControllerType();
            var paymentController = DependencyResolver.Current.GetService(paymentControllerType) as BasePaymentController;
            var warnings = paymentController.ValidatePaymentForm(fc);
            foreach (var warning in warnings)
                ModelState.AddModelError("", warning);
            if (ModelState.IsValid)
            {
                //get payment info
                var paymentInfo = paymentController.GetPaymentInfo(fc);
                //session save
                _httpContext.Session["OrderPaymentInfo"] = paymentInfo;

                var confirmOrderModel = PrepareConfirmOrderModel(cart);
                var jout = Json(new
                {
                    success = "true",
                    
                        html = this.RenderPartialViewToString("OpcConfirmOrder", confirmOrderModel)
                    
                });
                return jout;
            }
            }

                    //load next step



            catch (Exception ex)
            { }
            return Json(new { success = "true" });

        }


        [ValidateInput(false)]
        public ActionResult ProcessPayment()
        {
            Dictionary<string, string> sdkConfig = new Dictionary<string, string>();
            sdkConfig.Add("endpoint", "https://api.sandbox.paypal.com");
            sdkConfig.Add("mode", "sandbox");
            OAuthTokenCredential tokenCredential =
  new OAuthTokenCredential("Ad_GBRD0-iRQbHXvI6IQnWBxUr-KwPLYfwYs_m9h849hvDuEMaeKz5fZQ7WC", "EI-TLBBBlxeyGvkxixY6a0s5kEcGgOI00MYYqDf13DVGAoJ1xNZp_G42iHBc", sdkConfig);

                string accessToken = tokenCredential.GetAccessToken();

                var billingAddress = new PayPal.Api.Payments.Address();
                billingAddress.line1 = "52 N Main ST";
                billingAddress.city = "Johnstown";
                billingAddress.country_code = "US";
                billingAddress.postal_code = "43210";
                billingAddress.state = "OH";

                CreditCard creditCard = new CreditCard();
                creditCard.number = "6011748223015148";
                creditCard.type = "discover";
                creditCard.expire_month = 11;
                creditCard.expire_year = 2019;
                creditCard.cvv2 = 874;
                creditCard.first_name = "Owenn";
                creditCard.last_name = "Watsonn";
                creditCard.billing_address = billingAddress;

                Details amountDetails = new Details();
                amountDetails.subtotal = "7.41";
                amountDetails.tax = "0.03";
                amountDetails.shipping = "0.03";

                Amount amount = new Amount();
                amount.total = "7.47";
                amount.currency = "USD";
                amount.details = amountDetails;

                Transaction transaction = new Transaction();
                transaction.amount = amount;
                transaction.description = "This is the payment transaction description.";

                List<Transaction> transactions = new List<Transaction>();
                transactions.Add(transaction);

                FundingInstrument fundingInstrument = new FundingInstrument();
                fundingInstrument.credit_card = creditCard;

                List<FundingInstrument> fundingInstruments = new List<FundingInstrument>();
                fundingInstruments.Add(fundingInstrument);

                Payer payer = new Payer();
                payer.funding_instruments = fundingInstruments;
                payer.payment_method = "credit_card";

                Payment payment = new Payment();
                payment.intent = "sale";
                payment.payer = payer;
                payment.transactions = transactions;

                Payment createdPayment = payment.Create(accessToken);
                var outp = createdPayment;
          
            
            return Content("");
        }
        [ValidateInput(false)]
        public ActionResult OpcShippingAddressForm(string formcollection)
        {
            //var form = formcollection;
            System.Web.Script.Serialization.JavaScriptSerializer serializer = new System.Web.Script.Serialization.JavaScriptSerializer();

            AddressModel amodel = (AddressModel)serializer.Deserialize(formcollection, typeof(AddressModel));
             try
            {
                //validation
                var cart = _workContext.CurrentCustomer.ShoppingCartItems
                    .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                    .LimitPerStore(_storeContext.CurrentStore.Id)
                    .ToList();

                ////Pick up in store?
                //if (_shippingSettings.AllowPickUpInStore)
                //{
                //    var pickUpInStoreModel = new CheckoutShippingAddressModel();
                //    TryUpdateModel(pickUpInStoreModel);

                //    if (pickUpInStoreModel.PickUpInStore)
                //    {
                //        //customer decided to pick up in store

                //        //no shipping address selected
                //        _workContext.CurrentCustomer.ShippingAddress = null;
                //        _customerService.UpdateCustomer(_workContext.CurrentCustomer);

                //        //set value indicating that "pick up in store" option has been chosen
                //        _genericAttributeService.SaveAttribute(_workContext.CurrentCustomer, SystemCustomerAttributeNames.SelectedPickUpInStore, true, _storeContext.CurrentStore.Id);
                        
                //        //save "pick up in store" shipping method
                //        var pickUpInStoreShippingOption = new ShippingOption()
                //        {
                //            Name = _localizationService.GetResource("Checkout.PickUpInStore.MethodName"),
                //            Rate = decimal.Zero,
                //            Description = null,
                //            ShippingRateComputationMethodSystemName = null
                //        };
                //       _genericAttributeService.SaveAttribute(_workContext.CurrentCustomer,
                //        SystemCustomerAttributeNames.SelectedShippingOption,
                //        pickUpInStoreShippingOption,
                //        _storeContext.CurrentStore.Id);

                //        //load next step
                //        return OpcLoadStepAfterShippingMethod(cart);
                //    }

                //    //set value indicating that "pick up in store" option has not been chosen
                //    _genericAttributeService.SaveAttribute(_workContext.CurrentCustomer, SystemCustomerAttributeNames.SelectedPickUpInStore, false, _storeContext.CurrentStore.Id);
                //}

                //int shippingAddressId = 0;

                //    //new address
                    var model = new CheckoutShippingAddressModel();
                    model.NewAddress = amodel;
                //    TryUpdateModel(model.NewAddress, "ShippingNewAddress");
                    //validate model
                    TryValidateModel(model.NewAddress);
                    if (!ModelState.IsValid)
                    {
                        //model is not valid. redisplay the form with errors
                        var shippingAddressModel = PrepareShippingAddressModel(selectedCountryId: model.NewAddress.CountryId);
                        shippingAddressModel.NewAddressPreselected = true;
                        return Json(new
                        {
                            update_section = new UpdateSectionJsonModel()
                            {
                                name = "shipping",
                                html = this.RenderPartialViewToString("OpcShippingAddress", shippingAddressModel)
                            }
                        });
                    }

    
                        var address = model.NewAddress.ToEntity();
                        address.CreatedOnUtc = DateTime.UtcNow;
                        //little hack here (TODO: find a better solution)
                        //EF does not load navigation properties for newly created entities (such as this "Address").
                        //we have to load them manually 
                        //otherwise, "Country" property of "Address" entity will be null in shipping rate computation methods
                        if (address.CountryId.HasValue)
                            address.Country = _countryService.GetCountryById(address.CountryId.Value);
                        if (address.StateProvinceId.HasValue)
                            address.StateProvince = _stateProvinceService.GetStateProvinceById(address.StateProvinceId.Value);

                        //other null validations
                        if (address.CountryId == 0)
                            address.CountryId = null;
                        if (address.StateProvinceId == 0)
                            address.StateProvinceId = null;
                        _workContext.CurrentCustomer.Addresses.Add(address);
                    
                    _workContext.CurrentCustomer.ShippingAddress = address;
                 // Unless specifically asked to save ignore    _customerService.UpdateCustomer(_workContext.CurrentCustomer);
                    return OpcShippingMethodForm();
                }
            catch(Exception ex)
             {
                 return Content("");
             }

        }
       
        [ChildActionOnly]
        public ActionResult OpcShippingMethodForm()
        {
            try
            {
                var cart = _workContext.CurrentCustomer.ShoppingCartItems
.Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
.LimitPerStore(_storeContext.CurrentStore.Id)
.ToList();
                var shippingMethodModel = PrepareShippingMethodModel(cart);
                return PartialView("OpcShippingMethods", shippingMethodModel);
            }
            catch (Exception ex)
            {
                return Content("");
            }
        }
        [ValidateInput(false)]
        public ActionResult ProcessShippingMethod(string shippingMethodName)
        {
            var cart = _workContext.CurrentCustomer.ShoppingCartItems
.Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
.LimitPerStore(_storeContext.CurrentStore.Id)
.ToList();
            var shippingMethodModel = PrepareShippingMethodModel(cart);
            foreach (var method in shippingMethodModel.ShippingMethods)
            {
                if(method.Name.ToString().Equals(shippingMethodName))
                {
                   
                    _genericAttributeService.SaveAttribute(_workContext.CurrentCustomer,
                       SystemCustomerAttributeNames.SelectedShippingOption,
                       method.ShippingOption,
                       _storeContext.CurrentStore.Id);
                    break;
                }
            }
                           return Content("");

        }
        [ChildActionOnly]
        public ActionResult OpcPaymentMethodForm()
        {
            var cart = _workContext.CurrentCustomer.ShoppingCartItems
     .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
     .LimitPerStore(_storeContext.CurrentStore.Id)
     .ToList();
            var paymentMethodModel = PreparePaymentMethodModel(cart);
            return PartialView("OpcPaymentMethodsForm", paymentMethodModel);

        }
        [ChildActionOnly]
        [HttpPost]
        public ActionResult OpcPaymentInfoForm(IPaymentMethod paymentMethod)
        {
            var paymenInfoModel = PreparePaymentInfoModel(paymentMethod);
            return PartialView("OpcPaymentInfoForm", paymenInfoModel);

        }
        [ChildActionOnly]
        [HttpGet]
        public ActionResult OpcPaymentInfoForm(string val)
        {
            var cart = _workContext.CurrentCustomer.ShoppingCartItems
     .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
     .LimitPerStore(_storeContext.CurrentStore.Id)
     .ToList();
            var paymentMethodModel = PreparePaymentMethodModel(cart);
                                var selectedPaymentMethodSystemName = paymentMethodModel.PaymentMethods[0].PaymentMethodSystemName;
                    _genericAttributeService.SaveAttribute<string>(_workContext.CurrentCustomer,
                        SystemCustomerAttributeNames.SelectedPaymentMethod,
                        selectedPaymentMethodSystemName, _storeContext.CurrentStore.Id);
                   var pm = _paymentService.LoadPaymentMethodBySystemName(selectedPaymentMethodSystemName);
                    var paymenInfoModel = PreparePaymentInfoModel(pm);
            return PartialView("OpcPaymentInfoForm", paymenInfoModel);

        }
        [ValidateInput(false)]
        public ActionResult OpcSaveBilling(FormCollection form)
        {
            try
            {
                //validation
                var cart = _workContext.CurrentCustomer.ShoppingCartItems
                    .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                    .LimitPerStore(_storeContext.CurrentStore.Id)
                    .ToList();
                if (cart.Count == 0)
                    throw new Exception("Your cart is empty");

                if (!_orderSettings.OnePageCheckoutEnabled)
                    throw new Exception("One page checkout is disabled");

                if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                    throw new Exception("Anonymous checkout is not allowed");

                int billingAddressId = 0;
                int.TryParse(form["billing_address_id"], out billingAddressId);

                if (billingAddressId > 0)
                {
                    //existing address
                    var address = _workContext.CurrentCustomer.Addresses.FirstOrDefault(a => a.Id == billingAddressId);
                    if (address == null)
                        throw new Exception("Address can't be loaded");

                    _workContext.CurrentCustomer.BillingAddress = address;
                    _customerService.UpdateCustomer(_workContext.CurrentCustomer);
                }
                else
                {
                    //new address
                    var model = new CheckoutBillingAddressModel();
                    TryUpdateModel(model.NewAddress, "BillingNewAddress");
                    //validate model
                    TryValidateModel(model.NewAddress);
                    if (!ModelState.IsValid)
                    {
                        //model is not valid. redisplay the form with errors
                        var billingAddressModel = PrepareBillingAddressModel(selectedCountryId: model.NewAddress.CountryId);
                        billingAddressModel.NewAddressPreselected = true;
                        return Json(new
                        {
                            update_section = new UpdateSectionJsonModel()
                            {
                                name = "billing",
                                html = this.RenderPartialViewToString("OpcBillingAddress", billingAddressModel)
                            },
                            wrong_billing_address = true,
                        });
                    }

                    //try to find an address with the same values (don't duplicate records)
                    var address = _workContext.CurrentCustomer.Addresses.ToList().FindAddress(
                        model.NewAddress.FirstName, model.NewAddress.LastName, model.NewAddress.PhoneNumber,
                        model.NewAddress.Email, model.NewAddress.FaxNumber, model.NewAddress.Company,
                        model.NewAddress.Address1, model.NewAddress.Address2, model.NewAddress.City,
                        model.NewAddress.StateProvinceId, model.NewAddress.ZipPostalCode, model.NewAddress.CountryId);
                    if (address == null)
                    {
                        //address is not found. let's create a new one
                        address = model.NewAddress.ToEntity();
                        address.CreatedOnUtc = DateTime.UtcNow;
                        //some validation
                        if (address.CountryId == 0)
                            address.CountryId = null;
                        if (address.StateProvinceId == 0)
                            address.StateProvinceId = null;
                        if (address.CountryId.HasValue && address.CountryId.Value > 0)
                        {
                            address.Country = _countryService.GetCountryById(address.CountryId.Value);
                        }
                        _workContext.CurrentCustomer.Addresses.Add(address);
                    }
                    _workContext.CurrentCustomer.BillingAddress = address;
                    _customerService.UpdateCustomer(_workContext.CurrentCustomer);
                }

                if (cart.RequiresShipping())
                {
                    //shipping is required
                    var shippingAddressModel = PrepareShippingAddressModel(prePopulateNewAddressWithCustomerFields: true);
                    return Json(new
                    {
                        update_section = new UpdateSectionJsonModel()
                        {
                            name = "shipping",
                            html = this.RenderPartialViewToString("OpcShippingAddress", shippingAddressModel)
                        },
                        goto_section = "shipping"
                    });
                }
                else
                {
                    //shipping is not required
                    _genericAttributeService.SaveAttribute<ShippingOption>(_workContext.CurrentCustomer, SystemCustomerAttributeNames.SelectedShippingOption, null, _storeContext.CurrentStore.Id);

                    //load next step
                    return OpcLoadStepAfterShippingMethod(cart);
                }
            }
            catch (Exception exc)
            {
                _logger.Warning(exc.Message, exc, _workContext.CurrentCustomer);
                return Json(new { error = 1, message = exc.Message });
            }
        }

        [ValidateInput(false)]
        public ActionResult OpcSaveShipping(FormCollection form)
        {
            try
            {
                //validation
                var cart = _workContext.CurrentCustomer.ShoppingCartItems
                    .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                    .LimitPerStore(_storeContext.CurrentStore.Id)
                    .ToList();
                if (cart.Count == 0)
                    throw new Exception("Your cart is empty");

                if (!_orderSettings.OnePageCheckoutEnabled)
                    throw new Exception("One page checkout is disabled");

                if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                    throw new Exception("Anonymous checkout is not allowed");

                if (!cart.RequiresShipping())
                    throw new Exception("Shipping is not required");

                //Pick up in store?
                if (_shippingSettings.AllowPickUpInStore)
                {
                    var pickUpInStoreModel = new CheckoutShippingAddressModel();
                    TryUpdateModel(pickUpInStoreModel);

                    if (pickUpInStoreModel.PickUpInStore)
                    {
                        //customer decided to pick up in store

                        //no shipping address selected
                        _workContext.CurrentCustomer.ShippingAddress = null;
                        _customerService.UpdateCustomer(_workContext.CurrentCustomer);

                        //set value indicating that "pick up in store" option has been chosen
                        _genericAttributeService.SaveAttribute(_workContext.CurrentCustomer, SystemCustomerAttributeNames.SelectedPickUpInStore, true, _storeContext.CurrentStore.Id);
                        
                        //save "pick up in store" shipping method
                        var pickUpInStoreShippingOption = new ShippingOption()
                        {
                            Name = _localizationService.GetResource("Checkout.PickUpInStore.MethodName"),
                            Rate = decimal.Zero,
                            Description = null,
                            ShippingRateComputationMethodSystemName = null
                        };
                        _genericAttributeService.SaveAttribute(_workContext.CurrentCustomer,
                        SystemCustomerAttributeNames.SelectedShippingOption,
                        pickUpInStoreShippingOption,
                        _storeContext.CurrentStore.Id);

                        //load next step
                        return OpcLoadStepAfterShippingMethod(cart);
                    }

                    //set value indicating that "pick up in store" option has not been chosen
                    _genericAttributeService.SaveAttribute(_workContext.CurrentCustomer, SystemCustomerAttributeNames.SelectedPickUpInStore, false, _storeContext.CurrentStore.Id);
                }

                int shippingAddressId = 0;
                int.TryParse(form["shipping_address_id"], out shippingAddressId);

                if (shippingAddressId > 0)
                {
                    //existing address
                    var address = _workContext.CurrentCustomer.Addresses.FirstOrDefault(a => a.Id == shippingAddressId);
                    if (address == null)
                        throw new Exception("Address can't be loaded");

                    _workContext.CurrentCustomer.ShippingAddress = address;
                    _customerService.UpdateCustomer(_workContext.CurrentCustomer);
                }
                else
                {
                    //new address
                    var model = new CheckoutShippingAddressModel();
                    TryUpdateModel(model.NewAddress, "ShippingNewAddress");
                    //validate model
                    TryValidateModel(model.NewAddress);
                    if (!ModelState.IsValid)
                    {
                        //model is not valid. redisplay the form with errors
                        var shippingAddressModel = PrepareShippingAddressModel(selectedCountryId: model.NewAddress.CountryId);
                        shippingAddressModel.NewAddressPreselected = true;
                        return Json(new
                        {
                            update_section = new UpdateSectionJsonModel()
                            {
                                name = "shipping",
                                html = this.RenderPartialViewToString("OpcShippingAddress", shippingAddressModel)
                            }
                        });
                    }

                    //try to find an address with the same values (don't duplicate records)
                    var address = _workContext.CurrentCustomer.Addresses.ToList().FindAddress(
                        model.NewAddress.FirstName, model.NewAddress.LastName, model.NewAddress.PhoneNumber,
                        model.NewAddress.Email, model.NewAddress.FaxNumber, model.NewAddress.Company,
                        model.NewAddress.Address1, model.NewAddress.Address2, model.NewAddress.City,
                        model.NewAddress.StateProvinceId, model.NewAddress.ZipPostalCode, model.NewAddress.CountryId);
                    if (address == null)
                    {
                        address = model.NewAddress.ToEntity();
                        address.CreatedOnUtc = DateTime.UtcNow;
                        //little hack here (TODO: find a better solution)
                        //EF does not load navigation properties for newly created entities (such as this "Address").
                        //we have to load them manually 
                        //otherwise, "Country" property of "Address" entity will be null in shipping rate computation methods
                        if (address.CountryId.HasValue)
                            address.Country = _countryService.GetCountryById(address.CountryId.Value);
                        if (address.StateProvinceId.HasValue)
                            address.StateProvince = _stateProvinceService.GetStateProvinceById(address.StateProvinceId.Value);

                        //other null validations
                        if (address.CountryId == 0)
                            address.CountryId = null;
                        if (address.StateProvinceId == 0)
                            address.StateProvinceId = null;
                        _workContext.CurrentCustomer.Addresses.Add(address);
                    }
                    _workContext.CurrentCustomer.ShippingAddress = address;
                    _customerService.UpdateCustomer(_workContext.CurrentCustomer);
                }

                var shippingMethodModel = PrepareShippingMethodModel(cart);

                if (_shippingSettings.BypassShippingMethodSelectionIfOnlyOne &&
                    shippingMethodModel.ShippingMethods.Count == 1)
                {
                    //if we have only one shipping method, then a customer doesn't have to choose a shipping method
                    _genericAttributeService.SaveAttribute(_workContext.CurrentCustomer,
                        SystemCustomerAttributeNames.SelectedShippingOption,
                        shippingMethodModel.ShippingMethods.First().ShippingOption,
                        _storeContext.CurrentStore.Id);

                    //load next step
                    return OpcLoadStepAfterShippingMethod(cart);
                }
                else
                {
                    return Json(new
                    {
                        update_section = new UpdateSectionJsonModel()
                        {
                            name = "shipping-method",
                            html = this.RenderPartialViewToString("OpcShippingMethods", shippingMethodModel)
                        },
                        goto_section = "shipping_method"
                    });
                }
            }
            catch (Exception exc)
            {
                _logger.Warning(exc.Message, exc, _workContext.CurrentCustomer);
                return Json(new { error = 1, message = exc.Message });
            }
        }

        [ValidateInput(false)]
        public ActionResult OpcSaveShippingMethod(FormCollection form)
        {
            try
            {
                //validation
                var cart = _workContext.CurrentCustomer.ShoppingCartItems
                    .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                    .LimitPerStore(_storeContext.CurrentStore.Id)
                    .ToList();
                if (cart.Count == 0)
                    throw new Exception("Your cart is empty");

                if (!_orderSettings.OnePageCheckoutEnabled)
                    throw new Exception("One page checkout is disabled");

                if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                    throw new Exception("Anonymous checkout is not allowed");
                
                if (!cart.RequiresShipping())
                    throw new Exception("Shipping is not required");

                //parse selected method 
                string shippingoption = form["shippingoption"];
                if (String.IsNullOrEmpty(shippingoption))
                    throw new Exception("Selected shipping method can't be parsed");
                var splittedOption = shippingoption.Split(new string[] { "___" }, StringSplitOptions.RemoveEmptyEntries);
                if (splittedOption.Length != 2)
                    throw new Exception("Selected shipping method can't be parsed");
                string selectedName = splittedOption[0];
                string shippingRateComputationMethodSystemName = splittedOption[1];
                
                //find it
                //performance optimization. try cache first
                var shippingOptions = _workContext.CurrentCustomer.GetAttribute<List<ShippingOption>>(SystemCustomerAttributeNames.OfferedShippingOptions, _storeContext.CurrentStore.Id);
                if (shippingOptions == null || shippingOptions.Count == 0)
                {
                    //not found? let's load them using shipping service
                    shippingOptions = _shippingService
                        .GetShippingOptions(cart, _workContext.CurrentCustomer.ShippingAddress, shippingRateComputationMethodSystemName, _storeContext.CurrentStore.Id)
                        .ShippingOptions
                        .ToList();
                }
                else
                {
                    //loaded cached results. let's filter result by a chosen shipping rate computation method
                    shippingOptions = shippingOptions.Where(so => so.ShippingRateComputationMethodSystemName.Equals(shippingRateComputationMethodSystemName, StringComparison.InvariantCultureIgnoreCase))
                        .ToList();
                }
                
                var shippingOption = shippingOptions
                    .Find(so => !String.IsNullOrEmpty(so.Name) && so.Name.Equals(selectedName, StringComparison.InvariantCultureIgnoreCase));
                if (shippingOption == null)
                    throw new Exception("Selected shipping method can't be loaded");

                //save
                _genericAttributeService.SaveAttribute(_workContext.CurrentCustomer, SystemCustomerAttributeNames.SelectedShippingOption, shippingOption, _storeContext.CurrentStore.Id);

                //load next step
                return OpcLoadStepAfterShippingMethod(cart);
            }
            catch (Exception exc)
            {
                _logger.Warning(exc.Message, exc, _workContext.CurrentCustomer);
                return Json(new { error = 1, message = exc.Message });
            }
        }

        [ValidateInput(false)]
        public ActionResult OpcSavePaymentMethod(FormCollection form)
        {
            try
            {
                //validation
                var cart = _workContext.CurrentCustomer.ShoppingCartItems
                    .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                    .LimitPerStore(_storeContext.CurrentStore.Id)
                    .ToList();
                if (cart.Count == 0)
                    throw new Exception("Your cart is empty");

                if (!_orderSettings.OnePageCheckoutEnabled)
                    throw new Exception("One page checkout is disabled");

                if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                    throw new Exception("Anonymous checkout is not allowed");

                string paymentmethod = form["paymentmethod"];
                //payment method 
                if (String.IsNullOrEmpty(paymentmethod))
                    throw new Exception("Selected payment method can't be parsed");


                var model = new CheckoutPaymentMethodModel();
                TryUpdateModel(model);

                //reward points
                if (_rewardPointsSettings.Enabled)
                {
                    _genericAttributeService.SaveAttribute(_workContext.CurrentCustomer,
                        SystemCustomerAttributeNames.UseRewardPointsDuringCheckout, model.UseRewardPoints,
                        _storeContext.CurrentStore.Id);
                }

                //Check whether payment workflow is required
                bool isPaymentWorkflowRequired = IsPaymentWorkflowRequired(cart);
                if (!isPaymentWorkflowRequired)
                {
                    //payment is not required
                    _genericAttributeService.SaveAttribute<string>(_workContext.CurrentCustomer,
                        SystemCustomerAttributeNames.SelectedPaymentMethod, null, _storeContext.CurrentStore.Id);

                    var confirmOrderModel = PrepareConfirmOrderModel(cart);
                    return Json(new
                    {
                        update_section = new UpdateSectionJsonModel()
                        {
                            name = "confirm-order",
                            html = this.RenderPartialViewToString("OpcConfirmOrder", confirmOrderModel)
                        },
                        goto_section = "confirm_order"
                    });
                }

                var paymentMethodInst = _paymentService.LoadPaymentMethodBySystemName(paymentmethod);
                if (paymentMethodInst == null ||
                    !paymentMethodInst.IsPaymentMethodActive(_paymentSettings) ||
                    !_pluginFinder.AuthenticateStore(paymentMethodInst.PluginDescriptor, _storeContext.CurrentStore.Id))
                    throw new Exception("Selected payment method can't be parsed");

                //save
                _genericAttributeService.SaveAttribute<string>(_workContext.CurrentCustomer,
                    SystemCustomerAttributeNames.SelectedPaymentMethod, paymentmethod, _storeContext.CurrentStore.Id);

                return OpcLoadStepAfterPaymentMethod(paymentMethodInst, cart);
            }
            catch (Exception exc)
            {
                _logger.Warning(exc.Message, exc, _workContext.CurrentCustomer);
                return Json(new { error = 1, message = exc.Message });
            }
        }

        [ValidateInput(false)]
        public ActionResult OpcSavePaymentInfo(FormCollection form)
        {
            try
            {
                //validation
                var cart = _workContext.CurrentCustomer.ShoppingCartItems
                    .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                    .LimitPerStore(_storeContext.CurrentStore.Id)
                    .ToList();
                if (cart.Count == 0)
                    throw new Exception("Your cart is empty");

                if (!_orderSettings.OnePageCheckoutEnabled)
                    throw new Exception("One page checkout is disabled");

                if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                    throw new Exception("Anonymous checkout is not allowed");

                var paymentMethodSystemName = _workContext.CurrentCustomer.GetAttribute<string>(
                    SystemCustomerAttributeNames.SelectedPaymentMethod,
                    _genericAttributeService, _storeContext.CurrentStore.Id);
                var paymentMethod = _paymentService.LoadPaymentMethodBySystemName(paymentMethodSystemName);
                if (paymentMethod == null)
                    throw new Exception("Payment method is not selected");

                var paymentControllerType = paymentMethod.GetControllerType();
                var paymentController = DependencyResolver.Current.GetService(paymentControllerType) as BasePaymentController;
                var warnings = paymentController.ValidatePaymentForm(form);
                foreach (var warning in warnings)
                    ModelState.AddModelError("", warning);
                if (ModelState.IsValid)
                {
                    //get payment info
                    var paymentInfo = paymentController.GetPaymentInfo(form);
                    //session save
                    _httpContext.Session["OrderPaymentInfo"] = paymentInfo;

                    var confirmOrderModel = PrepareConfirmOrderModel(cart);
                    return Json(new
                    {
                        update_section = new UpdateSectionJsonModel()
                        {
                            name = "confirm-order",
                            html = this.RenderPartialViewToString("OpcConfirmOrder", confirmOrderModel)
                        },
                        goto_section = "confirm_order"
                    });
                }

                //If we got this far, something failed, redisplay form
                var paymenInfoModel = PreparePaymentInfoModel(paymentMethod);
                return Json(new
                {
                    update_section = new UpdateSectionJsonModel()
                    {
                        name = "payment-info",
                        html = this.RenderPartialViewToString("OpcPaymentInfo", paymenInfoModel)
                    }
                });
            }
            catch (Exception exc)
            {
                _logger.Warning(exc.Message, exc, _workContext.CurrentCustomer);
                return Json(new { error = 1, message = exc.Message });
            }
        }

        [ValidateInput(false)]
        public ActionResult OpcConfirmOrder()
        {
            try
            {
                //validation
                var cart = _workContext.CurrentCustomer.ShoppingCartItems
                    .Where(sci => sci.ShoppingCartType == ShoppingCartType.ShoppingCart)
                    .LimitPerStore(_storeContext.CurrentStore.Id)
                    .ToList();
                if (cart.Count == 0)
                    throw new Exception("Your cart is empty");

                if (!_orderSettings.OnePageCheckoutEnabled)
                    throw new Exception("One page checkout is disabled");

                if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                    throw new Exception("Anonymous checkout is not allowed");

                //prevent 2 orders being placed within an X seconds time frame
                if (!IsMinimumOrderPlacementIntervalValid(_workContext.CurrentCustomer))
                    throw new Exception(_localizationService.GetResource("Checkout.MinOrderPlacementInterval"));

                //place order
                var processPaymentRequest = _httpContext.Session["OrderPaymentInfo"] as ProcessPaymentRequest;
                if (processPaymentRequest == null)
                {
                    //Check whether payment workflow is required
                    if (IsPaymentWorkflowRequired(cart))
                    {
                        throw new Exception("Payment information is not entered");
                    }
                    else
                        processPaymentRequest = new ProcessPaymentRequest();
                }

                processPaymentRequest.StoreId = _storeContext.CurrentStore.Id;
                processPaymentRequest.CustomerId = _workContext.CurrentCustomer.Id;
                processPaymentRequest.PaymentMethodSystemName = _workContext.CurrentCustomer.GetAttribute<string>(
                    SystemCustomerAttributeNames.SelectedPaymentMethod,
                    _genericAttributeService, _storeContext.CurrentStore.Id);
                var placeOrderResult = _orderProcessingService.PlaceOrder(processPaymentRequest);
                if (placeOrderResult.Success)
                {
                    _httpContext.Session["OrderPaymentInfo"] = null;
                    var postProcessPaymentRequest = new PostProcessPaymentRequest()
                    {
                        Order = placeOrderResult.PlacedOrder
                    };


                    var paymentMethod = _paymentService.LoadPaymentMethodBySystemName(placeOrderResult.PlacedOrder.PaymentMethodSystemName);
                    if (paymentMethod != null)
                    {
                        if (paymentMethod.PaymentMethodType == PaymentMethodType.Redirection)
                        {
                            //Redirection will not work because it's AJAX request.
                            //That's why we don't process it here (we redirect a user to another page where he'll be redirected)

                            //redirect
                            return Json(new { redirect = string.Format("{0}checkout/OpcCompleteRedirectionPayment", _webHelper.GetStoreLocation()) });
                        }
                        else
                        {
                            _paymentService.PostProcessPayment(postProcessPaymentRequest);
                            //success
                            return Json(new { success = 1 });
                        }
                    }
                    else
                    {
                        //payment method could be null if order total is 0

                        //success
                        return Json(new { success = 1 });
                    }
                }
                else
                {
                    //error
                    var confirmOrderModel = new CheckoutConfirmModel();
                    foreach (var error in placeOrderResult.Errors)
                        confirmOrderModel.Warnings.Add(error); 
                    
                    return Json(new
                        {
                            update_section = new UpdateSectionJsonModel()
                            {
                                name = "confirm-order",
                                html = this.RenderPartialViewToString("OpcConfirmOrder", confirmOrderModel)
                            },
                            goto_section = "confirm_order"
                        });
                }
            }
            catch (Exception exc)
            {
                _logger.Warning(exc.Message, exc, _workContext.CurrentCustomer);
                return Json(new { error = 1, message = exc.Message });
            }
        }

        public ActionResult OpcCompleteRedirectionPayment()
        {
            try
            {
                //validation
                if (!_orderSettings.OnePageCheckoutEnabled)
                    return RedirectToRoute("HomePage");

                if ((_workContext.CurrentCustomer.IsGuest() && !_orderSettings.AnonymousCheckoutAllowed))
                    return new HttpUnauthorizedResult();

                //get the order
                var order = _orderService.SearchOrders(storeId: _storeContext.CurrentStore.Id,
                customerId: _workContext.CurrentCustomer.Id, pageSize: 1)
                    .FirstOrDefault();
                if (order == null)
                    return RedirectToRoute("HomePage");

                
                var paymentMethod = _paymentService.LoadPaymentMethodBySystemName(order.PaymentMethodSystemName);
                if (paymentMethod == null)
                    return RedirectToRoute("HomePage");
                if (paymentMethod.PaymentMethodType != PaymentMethodType.Redirection)
                    return RedirectToRoute("HomePage");

                //ensure that order has been just placed
                if ((DateTime.UtcNow - order.CreatedOnUtc).TotalMinutes > 3)
                    return RedirectToRoute("HomePage");


                //Redirection will not work on one page checkout page because it's AJAX request.
                //That's why we process it here
                var postProcessPaymentRequest = new PostProcessPaymentRequest()
                {
                    Order = order
                };

                _paymentService.PostProcessPayment(postProcessPaymentRequest);

                if (_webHelper.IsRequestBeingRedirected || _webHelper.IsPostBeingDone)
                {
                    //redirection or POST has been done in PostProcessPayment
                    return Content("Redirected");
                }
                else
                {
                    //if no redirection has been done (to a third-party payment page)
                    //theoretically it's not possible
                    return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
                }
            }
            catch (Exception exc)
            {
                _logger.Warning(exc.Message, exc, _workContext.CurrentCustomer);
                return Content(exc.Message);
            }
        }

        #endregion
    }
}
