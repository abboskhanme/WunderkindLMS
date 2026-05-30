using SchoolLms.Server.Dtos;
using SchoolLms.Server.Models;

namespace SchoolLms.Server.Services;

/// <summary>Oshxona kunlik menyusini (nonushta/tushlik/kechki) DTO'ga yig'uvchi yordamchi.</summary>
public static class CanteenMenu
{
    public static readonly string[] Meals = ["breakfast", "lunch", "dinner"];

    /// <summary>Bitta kun menyusi — ovqat turi bo'yicha guruhlangan taomlar.</summary>
    public static DayMenuDto BuildDay(string date, IEnumerable<Dish> dishes) =>
        new(date, Meals.ToDictionary(
            m => m,
            m => dishes.Where(d => d.Meal == m)
                       .Select(d => new DishDto(d.Id, d.Name, d.Ingredients, d.ImageUrl))
                       .ToList()));
}
