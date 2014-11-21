using System.Web.Mvc;
using Nop.Web.Framework.Security;

namespace Nop.Web.Controllers
{
    public partial class SearchController : BasePublicController
    {
        [NopHttpsRequirement(SslRequirement.No)]
        public ActionResult Index()
        {
            return View();
        }

        [NopHttpsRequirement(SslRequirement.No)]
        public ActionResult Index(string term)
        {
            return View();
        }
    }
}
