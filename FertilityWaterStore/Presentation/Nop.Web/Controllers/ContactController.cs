using System.Web.Mvc;
using Nop.Web.Framework.Security;

namespace Nop.Web.Controllers
{
    public partial class ContactController : BasePublicController
    {
        [NopHttpsRequirement(SslRequirement.No)]
        public ActionResult Index()
        {
            return View();
        }
        public ActionResult SendMessage(string name, string email, string comments)
        {
            return Json(new
            {
                success = "true"
            });
        }
    }
}
