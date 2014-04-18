﻿using System;
using System.Linq;
using Moq;
using NUnit.Framework;
using Rock.DependencyInjection;

namespace ConstructorSelectorTests
{
    public class ConstructorSelectorTestBase
    {
        protected Mock<IResolver> MockResolver { get; private set; }

        [SetUp]
        public void Setup()
        {
            MockResolver = new Mock<IResolver>();
        }

        protected void SetResolvable(Type type)
        {
            MockResolver
                .Setup(m => m.CanResolve(It.Is<Type>(t => t == type)))
                .Returns(true);
        }

        protected interface IInterface1 {}
        protected interface IInterface2 {}
        protected interface IInterface3 {}
        protected interface IInterface4 {}

        protected class Class1
        {
            public Class1(IInterface1 p1, IInterface2 p2) {}
            public Class1(IInterface3 p1, IInterface4 p2) {}
        }

        protected class Class2
        {
            public Class2(IInterface1 p1, IInterface2 p2) {}
            public Class2(IInterface3 p2) { }
        }

        protected class Class3
        {
            public Class3(IInterface1 p1, IInterface2 p2, int p3) {}
            public Class3(IInterface3 p2) {}
        }

        protected class Class4
        {
            public Class4(IInterface1 p1, IInterface2 p2) {}
            public Class4(IInterface3 p1, IInterface4 p2 = null) {}
        }
    }

    public class TheGetConstructorMethod : ConstructorSelectorTestBase
    {
        [Test]
        public void ChoosesAResolvableConstructor()
        {
            SetResolvable(typeof(IInterface1));
            SetResolvable(typeof(IInterface2));

            var sut = new ConstructorSelector();

            var ctor = sut.GetConstructor(typeof(Class1), MockResolver.Object);

            Assert.That(ctor.GetParameters().All(p => MockResolver.Object.CanResolve(p.ParameterType) || p.HasDefaultValue), Is.True);
        }

        [Test]
        public void ThrowsAnExceptionWhenNoConstructorIsResolvable()
        {
            var sut = new ConstructorSelector();

            Assert.That(() => sut.GetConstructor(typeof(Class1), MockResolver.Object), Throws.Exception);
        }

        [Test]
        public void ChoosesTheResolvableConstructorWithTheMostParameters()
        {
            SetResolvable(typeof(IInterface1));
            SetResolvable(typeof(IInterface2));
            SetResolvable(typeof(IInterface3));

            var sut = new ConstructorSelector();

            var ctor = sut.GetConstructor(typeof(Class2), MockResolver.Object);

            Assert.That(ctor.GetParameters().Length, Is.EqualTo(2));
        }

        [Test]
        public void NeverSelectsAConstructorWithANonDefaultPrimitiveParameter()
        {
            SetResolvable(typeof(IInterface1));
            SetResolvable(typeof(IInterface2));
            SetResolvable(typeof(IInterface3));

            var sut = new ConstructorSelector();

            var ctor = sut.GetConstructor(typeof(Class3), MockResolver.Object);

            Assert.That(ctor.GetParameters().Length, Is.EqualTo(1));
        }

        public class GivenTwoResolvableConstructorsWithTheSameNumberOfParameters : ConstructorSelectorTestBase
        {
            [Test]
            public void IfNoParametersAreDefaultAnExceptionIsThrown()
            {
                SetResolvable(typeof(IInterface1));
                SetResolvable(typeof(IInterface2));
                SetResolvable(typeof(IInterface3));
                SetResolvable(typeof(IInterface4));

                var sut = new ConstructorSelector();

                Assert.That(() => sut.GetConstructor(typeof(Class1), MockResolver.Object), Throws.Exception);
            }
        
            [Test]
            public void IfOneConstructorHasAParameterWithADefaultValueThenTheOtherConstructorIsSelected()
            {
                SetResolvable(typeof(IInterface1));
                SetResolvable(typeof(IInterface2));
                SetResolvable(typeof(IInterface3));
                SetResolvable(typeof(IInterface4));

                var sut = new ConstructorSelector();

                var ctor = sut.GetConstructor(typeof(Class4), MockResolver.Object);

                Assert.That(ctor.GetParameters().Select(p => p.ParameterType), Is.EquivalentTo(new[] { typeof(IInterface1), typeof(IInterface2) }));
            }
        }
    }
}