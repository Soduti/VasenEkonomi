using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Services;

namespace Vasen.Web.Security;

// Every non-owner write is denied unless explicitly allowed below.
public class AccessFilter(RequestAccess access, VasenDb db) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        await access.LoadAsync(context.HttpContext.User);
        if (context.HttpContext.User.Identity?.IsAuthenticated != true) { await next(); return; }
        var page = context.ActionDescriptor.ViewEnginePath;
        var account = page is "/Account/Profile" or "/Account/ChoosePassword" or "/Account/Logout" or "/Account/Login" or "/Account/AccessDenied";
        var allowed = account || access.IsOwner;
        if (!allowed && AccessRules.IsActive(access.Member))
        {
            if (page == "/Receipts/Edit" && context.HandlerInstance is Pages.Receipts.EditModel edit)
            {
                if (HttpMethods.IsGet(context.HttpContext.Request.Method) || HttpMethods.IsHead(context.HttpContext.Request.Method))
                    allowed = edit.Id.HasValue || access.CanRegisterReceipts;
                else if (HttpMethods.IsPost(context.HttpContext.Request.Method) && context.HandlerMethod?.MethodInfo.Name == "OnPostAsync")
                {
                    if (!edit.Id.HasValue) allowed = access.CanRegisterReceipts;
                    else
                    {
                        var receipt = await db.Receipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == edit.Id && x.DeletedUtc == null);
                        allowed = receipt != null && access.CanEdit(receipt);
                    }
                }
            }
            else if (HttpMethods.IsGet(context.HttpContext.Request.Method) || HttpMethods.IsHead(context.HttpContext.Request.Method))
            {
                allowed = page is "/Index" or "/Budget/Index" or "/Expenses/Index" or "/Deposits/Index"
                    or "/Withdrawals/Index" or "/Withdrawals/Details" or "/Receipts/File" or "/Error";
            }
        }
        if (!allowed) { context.Result = new ForbidResult(); return; }
        var executed = await next();
        if (executed.Exception is UnauthorizedAccessException)
        {
            executed.ExceptionHandled = true;
            executed.Result = new ForbidResult();
        }
    }
}
