namespace AutofixtureExtender
{
    using System;
    using System.Collections.Generic;
    using AutoFixture;
    using AutoFixture.Dsl;
    using AutoFixture.Kernel;

    public class FixtureExtend : Fixture
    {
        private readonly Dictionary<Type, List<Func<ISpecimenBuilder, ISpecimenBuilder>>> _specimenBuilders = new Dictionary<Type, List<Func<ISpecimenBuilder, ISpecimenBuilder>>>();

        public FixtureExtend()
        {
        }

        public FixtureExtend(DefaultRelays engineParts)
            : base(engineParts)
        {
        }

        public FixtureExtend(ISpecimenBuilder engine, MultipleRelay multiple)
            : base(engine, multiple)
        {
        }

        public void AddCustomization<T>(Func<IPostprocessComposer<T>, ISpecimenBuilder> composerTransformation)
        {
            this.Customize(composerTransformation);

            var key = typeof(T);
            Func<ISpecimenBuilder, ISpecimenBuilder> func = sb => composerTransformation.Invoke((IPostprocessComposer<T>)sb);

            if (!this._specimenBuilders.ContainsKey(key))
            {
                this._specimenBuilders[key] = new List<Func<ISpecimenBuilder, ISpecimenBuilder>>();
            }

            this._specimenBuilders[key].Add(func);
        }

        public IPostprocessComposer<T> BuildWith<T>()
        {
            IPostprocessComposer<T> build = this.Build<T>();

            var builders = this._specimenBuilders.GetValueOrDefault(typeof(T), new List<Func<ISpecimenBuilder, ISpecimenBuilder>>());
            foreach (var specimenBuilder in builders)
            {
                build = (IPostprocessComposer<T>)specimenBuilder(build);
            }

            //var abc = this.Customizations.OfType<IPostprocessComposer<T>>().ToList();
            //var build1 = this.Build<T>() as CompositeNodeComposer<T>;
            //CompositeNodeComposer<T> res = (CompositeNodeComposer<T>)build1.Compose(abc);

            //var entities = this.Build<Premise>()
            //  .With(i => i.PropertyName, "Test name")
            //  .Without(i => i.IncidentPremisesNavigation);
            return build;
        }
    }
}
