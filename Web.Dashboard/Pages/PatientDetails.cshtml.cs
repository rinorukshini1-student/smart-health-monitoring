using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.Dashboard.Pages;

public sealed class PatientDetailsModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string PatientId { get; set; } = "P001";

    public void OnGet()
    {
    }
}
