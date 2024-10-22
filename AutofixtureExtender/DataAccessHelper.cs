namespace AutofixtureExtender
{
    using AutoFixture;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    public static class DataAccessHelper
    {
        public static T CreateEf<T>(this IFixture fixture, DbContext context)
        {
            var dict = context.Model.GetEntityTypes()
                 .ToDictionary(g => g.ClrType, g => g);

            return (T)fixture.CreateEf(typeof(T), dict);
        }

        public static object CreateEf(this IFixture fixture, Type type, Dictionary<Type, IEntityType> dict)
        {
            var entityType = dict[type];

            var fixtureGenericMethod = typeof(DataAccessHelper)
                        .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                        .Where(mi => mi.Name == nameof(_create) && mi.IsGenericMethod)
                        .Single()
                        .MakeGenericMethod(entityType.ClrType);

            object obj = fixtureGenericMethod.Invoke(null, new object[] { fixture })!;
            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                var navigation = foreignKey.DependentToPrincipal!;

                var navigationObject = fixture.CreateEf(navigation.TargetEntityType.ClrType, dict);
                navigation.PropertyInfo!.SetValue(obj, navigationObject, null);
            }

            return obj;
        }

        static T _create<T>(IFixture fixture) => fixture.Create<T>();
    }
}
