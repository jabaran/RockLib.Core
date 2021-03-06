using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Rock.DependencyInjection.Heuristics;
using Rock.Immutable;

namespace Rock.DependencyInjection
{
    /// <summary>
    /// An implementation of <see cref="IResolver"/> that registers, at constructor-time,
    /// a collection of object instances. When the <see cref="Get{T}"/> or <see cref="Get"/>
    /// methods are called, these instances are available to be passed as a constructor
    /// arguments if the instance satisfies the constructor arg's contract.
    /// </summary>
    public partial class AutoContainer : IResolver
    {
        private const Func<object> _getInstanceFuncNotFound = null;

        private static readonly Semimutable<IResolverConstructorSelector> _defaultResolverConstructorSelector = new Semimutable<IResolverConstructorSelector>(GetDefaultDefaultResolverConstructorSelector);

        private static readonly MethodInfo _genericGetMethod;

        private readonly IEnumerable<object> _instances;
        private readonly ConcurrentDictionary<Type, Func<object>> _bindings;
        private readonly IResolverConstructorSelector _constructorSelector;

        static AutoContainer()
        {
            Expression<Func<AutoContainer, int>> expression = container => container.Get<int>();
            _genericGetMethod = ((MethodCallExpression)expression.Body).Method.GetGenericMethodDefinition();
        }

        private AutoContainer(
            IEnumerable<object> instances,
            ConcurrentDictionary<Type, Func<object>> bindings,
            IResolverConstructorSelector constructorSelector)
        {
            if (instances == null) { throw new ArgumentNullException("instances"); }
            if (bindings == null) { throw new ArgumentNullException("bindings"); }
            if (constructorSelector == null) { throw new ArgumentNullException("constructorSelector"); }

            _instances = instances;
            _bindings = bindings;
            _constructorSelector = constructorSelector;
        }

        /// <summary>
        /// Copy constructor. Initializes an instance of an inheritor of <see cref="AutoContainer"/>
        /// to have the same values for its private backing fields as <paramref name="parentContainer"/>.
        /// NOTE: The fields that are copied are the fields defined in in the <see cref="AutoContainer"/>
        /// class. In other words, fields defined in an inheritor of <see cref="AutoContainer"/> will not
        /// be copied by this constructor.
        /// </summary>
        protected AutoContainer(AutoContainer parentContainer)
            : this(parentContainer._instances, parentContainer._bindings, parentContainer._constructorSelector)
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="AutoContainer"/>, using the
        /// <paramref name="instances"/> as its registered dependencies. These
        /// depenendencies will be resolvable by this instance of
        /// <see cref="AutoContainer"/> via any type that exactly one dependency
        /// equals, implements, or inherits from. This instance of <see cref="AutoContainer"/>
        /// will use <see cref="DefaultResolverConstructorSelector"/> internally to determine
        /// which constructor of an arbitrary type will be selected for invocation when
        /// <see cref="Get{T}"/> or <see cref="Get"/> methods are called.
        /// </summary>
        /// <param name="instances">The objects to use as registered dependencies.</param>
        public AutoContainer(params object[] instances)
            : this(null, instances)
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="AutoContainer"/>, using the
        /// <paramref name="instances"/> as its registered dependencies. These
        /// depenendencies will be resolvable by this instance of
        /// <see cref="AutoContainer"/> via any type that exactly one dependency
        /// equals, implements, or inherits from. This instance of <see cref="AutoContainer"/>
        /// will use the <see cref="IResolverConstructorSelector"/> specified by
        /// <paramref name="constructorSelector"/> internally to determine
        /// which constructor of an arbitrary type will be selected for invocation when
        /// <see cref="Get{T}"/> or <see cref="Get"/> methods are called.
        /// </summary>
        /// <param name="constructorSelector">
        /// An object that determines which constructor should be used when creating an instance of a type.
        /// If null, the value of the <see cref="DefaultResolverConstructorSelector"/> property is used.
        /// </param>
        /// <param name="instances">The objects to use as registered dependencies.</param>
        public AutoContainer(IResolverConstructorSelector constructorSelector, IEnumerable<object> instances)
            : this(
                (instances ?? new object[0]).Where(x => x != null).ToList(), // Filter out nulls and .ToList() it for fast enumeration.
                new ConcurrentDictionary<Type, Func<object>>(),
                constructorSelector ?? DefaultResolverConstructorSelector)
        {
        }

        /// <summary>
        /// Gets the default instance of <see cref="IResolverConstructorSelector"/>. Used by the
        /// constructors of the <see cref="AutoContainer"/> class when the
        /// <see cref="IResolverConstructorSelector"/> parameter is null or not present.
        /// </summary>
        public static IResolverConstructorSelector DefaultResolverConstructorSelector
        {
            get { return _defaultResolverConstructorSelector.Value; }
        }

        /// <summary>
        /// Sets the default <see cref="IResolverConstructorSelector"/>. If the
        /// <see cref="DefaultResolverConstructorSelector"/> has been accessed, then calls to this method
        /// have no effect.
        /// </summary>
        /// <param name="resolverConstructorSelector">
        /// The value that <see cref="DefaultResolverConstructorSelector"/> property should return. Ignored
        /// if the <see cref="DefaultResolverConstructorSelector"/> property has previously been accessed.
        /// </param>
        public static void SetDefaultResolverConstructorSelector(IResolverConstructorSelector resolverConstructorSelector)
        {
            _defaultResolverConstructorSelector.Value = resolverConstructorSelector;
        }

        internal static void ResetDefaultResolverConstructorSelector()
        {
            UnlockDefaultResolverConstructorSelector();
            _defaultResolverConstructorSelector.ResetValue();
        }

        internal static void UnlockDefaultResolverConstructorSelector()
        {
            _defaultResolverConstructorSelector.GetUnlockValueMethod().Invoke(_defaultResolverConstructorSelector, null);
        }

        private static IResolverConstructorSelector GetDefaultDefaultResolverConstructorSelector()
        {
            return new ResolverConstructorSelector();
        }

        /// <summary>
        /// Returns whether this instance of <see cref="AutoContainer"/> can get an instance of the specified
        /// type.
        /// </summary>
        /// <param name="type">The type to determine whether this instance is able to get an instance of.</param>
        /// <returns>True, if this instance can get an instance of the specified type. False, otherwise.</returns>
        public virtual bool CanGet(Type type)
        {
            return GetGetInstanceFunc(type) != _getInstanceFuncNotFound;
        }

        /// <summary>
        /// Gets an instance of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The type of object to return.</typeparam>
        /// <returns>An instance of type <typeparamref name="T"/>.</returns>
        public T Get<T>()
        {
            return (T)Get(typeof(T));
        }

        /// <summary>
        /// Gets an instance of type <paramref name="type"/>.
        /// </summary>
        /// <param name="type">The type of object to return.</param>
        /// <returns>An instance of type <paramref name="type"/></returns>
        public virtual object Get(Type type)
        {
            var getInstanceFunc = GetGetInstanceFunc(type);

            if (getInstanceFunc == _getInstanceFuncNotFound)
            {
                throw new ResolveException("Cannot resolve type: " + type);
            }

            return getInstanceFunc();
        }

        IResolver IResolver.MergeWith(IResolver otherContainer)
        {
            return MergeWith(otherContainer);
        }

        /// <summary>
        /// Returns a new instance of <see cref="AutoContainer"/> that is the result of a merge operation between
        /// this instance of <see cref="AutoContainer"/> and <paramref name="secondaryResolver"/>.
        /// </summary>
        /// <param name="secondaryResolver">A secondary <see cref="IResolver"/>.</param>
        /// <returns>An instance of <see cref="AutoContainer"/> resulting from the merge operation.</returns>
        public virtual AutoContainer MergeWith(IResolver secondaryResolver)
        {
            return new MergedAutoContainer(this, secondaryResolver);
        }

        /// <summary>
        /// Explicitly set the binding. When the <paramref name="contractType"/> type
        /// needs to be resolved, AutoContainer will resolve it using
        /// <paramref name="implementationType"/>.
        /// </summary>
        /// <param name="contractType">The type of the contract.</param>
        /// <param name="implementationType">The type of the implementation.</param>
        public void SetBinding(Type contractType, Type implementationType)
        {
            var getInstanceFunc = CreateGetInstanceFunc(implementationType);

            _bindings.AddOrUpdate(
                contractType,
                type => getInstanceFunc,
                (type, func) => getInstanceFunc);
        }

        /// <summary>
        /// Returns <see cref="_getInstanceFuncNotFound"/> if the type is unresolvable.
        /// </summary>
        private Func<object> GetGetInstanceFunc(Type type)
        {
            return _bindings.GetOrAdd(type, CreateGetInstanceFunc);
        }

        private Func<object> CreateGetInstanceFunc(Type type)
        {
            if (type == typeof(object))
            {
                return () => new object();
            }

            object instance;
            if (TryGetInstance(type, out instance))
            {
                return () => instance;
            }

            ConstructorInfo constructor;
            if (_constructorSelector.TryGetConstructor(type, this, out constructor))
            {
                return GetCreateInstanceFunc(constructor);
            }

            return _getInstanceFuncNotFound;
        }

        private bool TryGetInstance(Type type, out object instance)
        {
            if (_instances.Count(type.IsInstanceOfType) == 1)
            {
                instance = _instances.First(type.IsInstanceOfType);
                return true;
            }

            instance = null;
            return false;
        }

        private Func<object> GetCreateInstanceFunc(ConstructorInfo ctor)
        {
            // We're going to be creating a lambda expression of type Func<object>.
            // Then we'lle compile that expression and return the resulting Func<object>.

            // We want a closure around 'this' so that we can access its Get<> method.
            var thisExpression = Expression.Constant(this);

            // The following is roughly what's going on in the lambda expression below.
            // Note that the select statement will be fully realized at expression creation time.
            // () =>
            //     new instance_type(
            //         for each of the constructor's parameters
            //             if the parameter's type can be resolved
            //                 use this.Get<parameter_type>()
            //             else
            //                 use the parameter's default value)

            var createInstanceExpression =
                Expression.Lambda<Func<object>>(
                    Expression.New(ctor,
                        ctor.GetParameters()
                            .Select(p =>
                                CanGet(p.ParameterType)
                                    ? (Expression)Expression.Call(
                                        thisExpression,
                                        _genericGetMethod.MakeGenericMethod(p.ParameterType))
                                    : Expression.Constant(p.DefaultValue, p.ParameterType))));

            return createInstanceExpression.Compile();
        }
    }
}