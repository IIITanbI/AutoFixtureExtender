namespace AutofixtureExtender
{
    using AutoFixture;
    using AutoFixture.Dsl;
    using AutoFixture.Kernel;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Infrastructure;
    using Microsoft.EntityFrameworkCore.Metadata;
    using Microsoft.EntityFrameworkCore.SqlServer.Metadata.Internal;
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;

    public class AutoFixtureExtender
    {
        private readonly Dictionary<Type, List<object>> _userTypes = new Dictionary<Type, List<object>>();

        public IReadOnlyDictionary<Type, List<object>> UserTypes => _userTypes.ToImmutableDictionary();

        public AutoFixtureExtender()
        {
        }

        public virtual Dictionary<Type, Action<IFixture>> DoWork(DbContext context)
        {
            var dictProps = context.Model.GetEntityTypes()
                .GroupBy(et => et.ClrType, et => et.GetProperties())
                .ToDictionary(g => g.Key, g => g.SelectMany(v => v).ToList());

            var customizations = dictProps.ToDictionary(kvp => kvp.Key, kvp => CreateCustomization(kvp.Key, kvp.Value));
            return customizations;
        }

        public virtual Action<IFixture> CreateCustomization(Type entityType, IEnumerable<IReadOnlyProperty> entityProperties)
        {
            var validProperties = entityProperties.Where(p => !p.IsShadowProperty());
            if (validProperties.Select(p => p.PropertyInfo.DeclaringType).Append(entityType).ToHashSet().Count > 1)
            {
                throw new Exception("Mismatch entity types");
            }

            var genericMethod = typeof(AutoFixtureExtender)
                .GetMethod(nameof(AutoFixtureExtender.ApplyComposer))
                .MakeGenericMethod(entityType);

            // return Func<ICustomizationComposer<TEntity>, ISpecimenBuilder>>
            var composerTransformation = genericMethod.Invoke(this, new object[] { validProperties });

            // Customize<T>
            var fixtureGenericMethod = typeof(IFixture)
                                    .GetMethods()
                                    .Where(mi => mi.Name == nameof(IFixture.Customize) && mi.IsGenericMethod)
                                    .Single()
                                    .MakeGenericMethod(entityType);

            void action(IFixture fixture)
            {
                // Func<ICustomizationComposer<TEntity>, ISpecimenBuilder>>
                fixtureGenericMethod.Invoke(fixture, new object[] { composerTransformation });
            }

            return action;
        }

        public virtual Func<ICustomizationComposer<TEntity>, ISpecimenBuilder> ApplyComposer<TEntity>(IEnumerable<IReadOnlyProperty> properties)
        {
            ISpecimenBuilder composerTransformation(IPostprocessComposer<TEntity> composer)
            {
                var composers = properties.Select(property => CreateFunc<TEntity>(property)).ToList();

                var userComposers = _userTypes.GetValueOrDefault(typeof(TEntity), new List<object>());
                composers.AddRange(userComposers.Select(c => (Func<IPostprocessComposer<TEntity>, IPostprocessComposer<TEntity>>)c));

                composers = composers.Where(x => x != null).ToList();

                var result = composer;
                foreach (var composerFunc in composers)
                {
                    result = composerFunc(result);
                }

                return result;
            }

            return composerTransformation;
        }

        public virtual Func<IPostprocessComposer<TEntity>, IPostprocessComposer<TEntity>> CreateFunc<TEntity>(IReadOnlyProperty property)
        {
            Func<object> factory = CreateFactory(property);

            if (factory == null)
            {
                return null;
            }

            return CreateFuncWithFactory<TEntity>(property, factory);
        }

        public virtual Func<IPostprocessComposer<TEntity>, IPostprocessComposer<TEntity>> CreateFuncWithFactory<TEntity>(IReadOnlyProperty property, Func<object> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            Expression<Func<TEntity, object>> propertyPicker = CreatePropertyPicker<TEntity>(property.PropertyInfo);
            IPostprocessComposer<TEntity> resultFunc(IPostprocessComposer<TEntity> composer) => composer.With(propertyPicker, factory);

            return resultFunc;
        }

        public virtual Func<object> CreateFactory(IReadOnlyProperty property)
        {
            int? maxLength = property.GetMaxLength();
            if (maxLength.HasValue)
            {
                return () => NewString(maxLength.Value);
            }

            if (IsIdentity(property))
            {
                return () => property.ClrType.IsValueType ? Activator.CreateInstance(property.ClrType) : null;
            }

            return null;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "<Pending>")]
        protected static bool IsIdentity(IReadOnlyAnnotatable operation)
        {
            return operation[SqlServerAnnotationNames.Identity] != null
                || operation[SqlServerAnnotationNames.ValueGenerationStrategy] as SqlServerValueGenerationStrategy?
                == SqlServerValueGenerationStrategy.IdentityColumn;
        }

        public virtual Expression<Func<TEntity, object>> CreatePropertyPicker<TEntity>(PropertyInfo propertyInfo)
        {
            var instanceParam = Expression.Parameter(propertyInfo.DeclaringType, "obj");
            var property = Expression.Property(instanceParam, propertyInfo);

            var conversion = Expression.Convert(property, typeof(object));
            var lambda = Expression.Lambda<Func<TEntity, object>>(conversion, instanceParam);

            return lambda;
        }

        public virtual void AddComposer<T>(Func<IPostprocessComposer<T>, IPostprocessComposer<T>> composer)
        {
            var type = typeof(T);
            if (!_userTypes.ContainsKey(type))
            {
                _userTypes[type] = new List<object>();
            }

            _userTypes[type].Add(composer);
        }

        public static string NewString(int length)
        {
            const int singleStringLength = 32;
            var count = (length + singleStringLength - 1) / singleStringLength;

            var strings = Enumerable.Range(0, count).Select(_ => Guid.NewGuid().ToString("N"));
            return string.Join(null, strings).Substring(0, length);
        }
    }
}