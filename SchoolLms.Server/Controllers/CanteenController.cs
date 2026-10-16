using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Application.Services;

namespace SchoolLms.Server.Controllers;

[ApiController]
[Authorize]
[AdminPerm("app")]
[Route("api/admin/canteen")]
public class CanteenController(AppDbContext db) : ControllerBase
{
    private static readonly string[] Meals = ["breakfast", "lunch", "dinner"];

    private static DayMenuDto BuildDay(string date, IEnumerable<Dish> dishes)
    {
        var meals = Meals.ToDictionary(
            m => m,
            m => dishes.Where(d => d.Meal == m)
                       .Select(d => new DishDto(d.Id, d.Name, d.Ingredients, d.ImageUrl))
                       .ToList());
        return new DayMenuDto(date, meals);
    }

    [HttpGet("{date}")]
    public async Task<ActionResult<DayMenuDto>> GetDay(string date)
    {
        var dishes = await db.Dishes.Where(d => d.Date == date).ToListAsync();
        return BuildDay(date, dishes);
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DayMenuDto>>> GetRange([FromQuery] string start, [FromQuery] string end)
    {
        var dishes = await db.Dishes
            .Where(d => string.Compare(d.Date, start) >= 0 && string.Compare(d.Date, end) <= 0)
            .ToListAsync();

        var result = new List<DayMenuDto>();
        var cur = start;
        while (string.CompareOrdinal(cur, end) <= 0)
        {
            result.Add(BuildDay(cur, dishes.Where(d => d.Date == cur)));
            cur = ScheduleMath.AddDaysISO(cur, 1);
        }
        return result;
    }

    [HttpPost("{date}/{meal}")]
    public async Task<ActionResult<DishDto>> Create(string date, string meal, DishPayload p)
    {
        if (!Meals.Contains(meal)) return BadRequest("Noto'g'ri ovqat turi");
        var dish = new Dish { Date = date, Meal = meal, Name = p.Name, Ingredients = p.Ingredients, ImageUrl = p.ImageUrl };
        db.Dishes.Add(dish);
        await db.SaveChangesAsync();
        return new DishDto(dish.Id, dish.Name, dish.Ingredients, dish.ImageUrl);
    }

    /// <summary>
    /// Copy a week's menu (Mon..Sun) onto the next <c>weeks</c> weeks starting at <c>toWeekStart</c>.
    /// 409 names the target days that already have a menu unless <c>overwrite</c> is set.
    /// The literal route outranks <c>{date}/{meal}</c> above. See docs/modules/canteen.md.
    /// </summary>
    [HttpPost("menu/copy-week")]
    public async Task<ActionResult<CanteenCopyWeekResultDto>> CopyWeek(CanteenCopyWeekPayload p, CancellationToken ct)
    {
        var outcome = await CanteenWeekCopy.RunAsync(db, p, ct);
        if (outcome.Status == CanteenWeekCopy.Status.Invalid)
            return BadRequest(new { message = outcome.Message });
        if (outcome.Status == CanteenWeekCopy.Status.Conflict)
            return Conflict(new { message = outcome.Message, days = outcome.ConflictDates });
        return outcome.Result!;
    }

    [HttpPut("{date}/{meal}/{dishId}")]
    public async Task<IActionResult> Update(string date, string meal, string dishId, DishPayload p)
    {
        var dish = await db.Dishes.FindAsync(dishId);
        if (dish is null) return NotFound();
        dish.Name = p.Name;
        dish.Ingredients = p.Ingredients;
        dish.ImageUrl = p.ImageUrl;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{date}/{meal}/{dishId}")]
    public async Task<IActionResult> Delete(string date, string meal, string dishId)
    {
        var dish = await db.Dishes.FindAsync(dishId);
        if (dish is null) return NotFound();
        db.Dishes.Remove(dish);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
