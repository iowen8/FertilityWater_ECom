using FluentValidation;
using Nop.Core.Domain.Common;
using Nop.Services.Localization;
using Nop.Web.Models.Common;

namespace Nop.Web.Validators.Common
{
    public class MyPaymentValidator : AbstractValidator<MyPaymentModel>
    {
        public MyPaymentValidator(ILocalizationService localizationService)
        {
            RuleFor(x => x.CardholderName)
                .NotEmpty()
                .WithMessage(localizationService.GetResource("MyPayment.Fields.CardholderName.Required"));
            RuleFor(x => x.CardNumber)
                .NotEmpty().CreditCard()
                .WithMessage(localizationService.GetResource("Payment.CardNumber.Wrong"));
            RuleFor(x => x.CardCode)
                .NotEmpty().Length(3).Matches("^(0|[1-9][0-9]*)$")
                .WithMessage(localizationService.GetResource("Payment.CardCode.Wrong"));
            RuleFor(x => x.ExpireMonth)
                .NotEmpty().GreaterThan(0).LessThanOrEqualTo(12)
                .WithMessage(localizationService.GetResource("Payment.ExpireMonth.Wrong"));
            RuleFor(x => x.ExpireYear)
    .NotEmpty().GreaterThanOrEqualTo(System.DateTime.Now.Year)
    .WithMessage(localizationService.GetResource("Payment.ExpireYear.Wrong"));
        }
    }
}