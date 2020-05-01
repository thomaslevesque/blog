---
layout: post
title: '[WPF] Using Linq to shape data in a CollectionView'
date: 2011-11-30T13:09:45.0000000
url: /2011/11/30/wpf-using-linq-to-shape-data-in-a-collectionview/
tags:
  - collectionview
  - linq
  - WPF
categories:
  - WPF
---

WPF provides a simple mechanism for shaping collections of data, via the `ICollectionView` interface and its `Filter`, `SortDescriptions` and `GroupDescriptions` properties:   
```csharp
// Collection to which the view is bound
public ObservableCollection People { get; private set; }
...

// Default view of the People collection
ICollectionView view = CollectionViewSource.GetDefaultView(People);

// Show only adults
view.Filter = o => ((Person)o).Age >= 18;

// Sort by last name and first name
view.SortDescriptions.Add(new SortDescription("LastName", ListSortDirection.Ascending));
view.SortDescriptions.Add(new SortDescription("FirstName", ListSortDirection.Ascending));

// Group by country
view.GroupDescriptions.Add(new PropertyGroupDescription("Country"));
```
  Even though this technique is not difficult to use, it has a few drawbacks: 
- The syntax is a bit clumsy and unnatural: the fact that the filter parameter is an `object` whereas we know it's actually of type `Person` makes the code less readable because of the cast, and the specification of the sort and group descriptions is a little repetitive
- Specifying the property names as strings introduces a risk of error, since they're not verified by the compiler

  In the last few years, we got used to use Linq to do this kind of things… it would be nice to be able to do the same for the shaping of an `ICollectionView`.  Let's see what syntax we could use to do it with Linq… something like this perhaps?  
```csharp
People.Where(p => p.Age >= 18)
      .OrderBy(p => p.LastName)
      .ThenBy(p => p.FirstName)
      .GroupBy(p => p.Country);
```
  Or, with the Linq query comprehension syntax:  
```csharp
from p in People
where p.Age >= 18
orderby p.LastName, p.FirstName
group p by p.Country;
```
  Obviously, this is not enough: this code only creates a  query on the collection, it doesn't modify the `CollectionView`… but with just a little extra work, we can get the desired result:  
```csharp
var query =
    from p in People.ShapeView()
    where p.Age >= 18
    orderby p.LastName, p.FirstName
    group p by p.Country;

query.Apply();
```
  The `ShapeView` method returns a wrapper which encapsulates the default view of the collection, and exposes `Where`, `OrderBy` and `GroupBy` methods with appropriate signatures to specify the shaping of the `CollectionView`. Creating the query has no direct effect, the changes are only applied to the view when  `Apply` is called: that's because it's better to apply all changes at the same time, using `ICollectionView.DeferRefresh`, to avoid causing a refresh of the view for each clause of the query. When `Apply` is called, we can see that the view is correctly updated to reflect the query.  This solution allows to define the filter, sort and grouping in a strongly-typed way, which implies that the code is verified by the compiler. It's also more concise and readable than the original code… Just be careful with one thing: some queries that are correct from the compiler's point of view won't be applicable to a `CollectionView`. For instance, if you try to group the data by the first letter of the last name (`p.LastName.Substring(0, 1)`), the `GroupBy` method will fail because only properties are supported by `PropertyGroupDescription`.  Note that the wrapper won't overwrite the shaping properties of the `CollectionView` if you don't specify the corresponding Linq clause, so you can just modify the current view without specifying everything again. If you need to clear the properties, you can use the `ClearFilter`, `ClearSort` and `ClearGrouping` methods:  
```csharp
// Remove the grouping and add a sort criteria
People.ShapeView()
      .ClearGrouping()
      .OrderBy(p => p.LastName);
      .Apply();
```
  Note that as for a normal Linq query, it's possible to use either the query comprehension syntax or to call the methods directly, since the former is just syntactic sugar for the latter.  Finally, here's the complete code of the wrapper and the associated extension methods:  
```csharp
    public static class CollectionViewShaper
    {
        public static CollectionViewShaper<TSource> ShapeView<TSource>(this IEnumerable<TSource> source)
        {
            var view = CollectionViewSource.GetDefaultView(source);
            return new CollectionViewShaper<TSource>(view);
        }

        public static CollectionViewShaper<TSource> Shape<TSource>(this ICollectionView view)
        {
            return new CollectionViewShaper<TSource>(view);
        }
    }

    public class CollectionViewShaper<TSource>
    {
        private readonly ICollectionView _view;
        private Predicate<object> _filter;
        private readonly List<SortDescription> _sortDescriptions = new List<SortDescription>();
        private readonly List<GroupDescription> _groupDescriptions = new List<GroupDescription>();

        public CollectionViewShaper(ICollectionView view)
        {
            if (view == null)
                throw new ArgumentNullException("view");
            _view = view;
            _filter = view.Filter;
            _sortDescriptions = view.SortDescriptions.ToList();
            _groupDescriptions = view.GroupDescriptions.ToList();
        }

        public void Apply()
        {
            using (_view.DeferRefresh())
            {
                _view.Filter = _filter;
                _view.SortDescriptions.Clear();
                foreach (var s in _sortDescriptions)
                {
                    _view.SortDescriptions.Add(s);
                }
                _view.GroupDescriptions.Clear();
                foreach (var g in _groupDescriptions)
                {
                    _view.GroupDescriptions.Add(g);
                }
            }
        }
            
        public CollectionViewShaper<TSource> ClearGrouping()
        {
            _groupDescriptions.Clear();
            return this;
        }

        public CollectionViewShaper<TSource> ClearSort()
        {
            _sortDescriptions.Clear();
            return this;
        }

        public CollectionViewShaper<TSource> ClearFilter()
        {
            _filter = null;
            return this;
        }

        public CollectionViewShaper<TSource> ClearAll()
        {
            _filter = null;
            _sortDescriptions.Clear();
            _groupDescriptions.Clear();
            return this;
        }

        public CollectionViewShaper<TSource> Where(Func<TSource, bool> predicate)
        {
            _filter = o => predicate((TSource)o);
            return this;
        }

        public CollectionViewShaper<TSource> OrderBy<TKey>(Expression<Func<TSource, TKey>> keySelector)
        {
            return OrderBy(keySelector, true, ListSortDirection.Ascending);
        }

        public CollectionViewShaper<TSource> OrderByDescending<TKey>(Expression<Func<TSource, TKey>> keySelector)
        {
            return OrderBy(keySelector, true, ListSortDirection.Descending);
        }

        public CollectionViewShaper<TSource> ThenBy<TKey>(Expression<Func<TSource, TKey>> keySelector)
        {
            return OrderBy(keySelector, false, ListSortDirection.Ascending);
        }

        public CollectionViewShaper<TSource> ThenByDescending<TKey>(Expression<Func<TSource, TKey>> keySelector)
        {
            return OrderBy(keySelector, false, ListSortDirection.Descending);
        }

        private CollectionViewShaper<TSource> OrderBy<TKey>(Expression<Func<TSource, TKey>> keySelector, bool clear, ListSortDirection direction)
        {
            string path = GetPropertyPath(keySelector.Body);
            if (clear)
                _sortDescriptions.Clear();
            _sortDescriptions.Add(new SortDescription(path, direction));
            return this;
        }

        public CollectionViewShaper<TSource> GroupBy<TKey>(Expression<Func<TSource, TKey>> keySelector)
        {
            string path = GetPropertyPath(keySelector.Body);
            _groupDescriptions.Add(new PropertyGroupDescription(path));
            return this;
        }

        private static string GetPropertyPath(Expression expression)
        {
            var names = new Stack<string>();
            var expr = expression;
            while (expr != null && !(expr is ParameterExpression) && !(expr is ConstantExpression))
            {
                var memberExpr = expr as MemberExpression;
                if (memberExpr == null)
                    throw new ArgumentException("The selector body must contain only property or field access expressions");
                names.Push(memberExpr.Member.Name);
                expr = memberExpr.Expression;
            }
            return String.Join(".", names.ToArray());
        }
    }
```

