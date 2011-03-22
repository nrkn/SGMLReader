using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SgmlReaderDll.Parser.Enums;

namespace SgmlReaderDll.Parser {
  /// <summary>
  /// Defines a group of elements nested within another element.
  /// </summary>
  public class Group
  {
    private readonly ArrayList _members;
    private GroupType _groupType;
    private bool _mixed;

    /// <summary>
    /// The <see cref="Occurrence"/> of this group.
    /// </summary>
    public Occurrence Occurrence { get; private set; }

    /// <summary>
    /// Checks whether the group contains only text.
    /// </summary>
    /// <value>true if the group is of mixed content and has no members, otherwise false.</value>
    public bool TextOnly
    {
      get
      {
        return _mixed && _members.Count == 0;
      }
    }

    /// <summary>
    /// The parent group of this group.
    /// </summary>
    public Group Parent { get; private set; }

    /// <summary>
    /// Initialises a new Content Model Group.
    /// </summary>
    /// <param name="parent">The parent model group.</param>
    public Group(Group parent)
    {
      Parent = parent;
      _members = new ArrayList();
      _groupType = GroupType.None;
      Occurrence = Occurrence.Required;
    }

    /// <summary>
    /// Adds a new child model group to the end of the group's members.
    /// </summary>
    /// <param name="g">The model group to add.</param>
    public void AddGroup(Group g)
    {
      _members.Add(g);
    }

    /// <summary>
    /// Adds a new symbol to the group's members.
    /// </summary>
    /// <param name="sym">The symbol to add.</param>
    public void AddSymbol(string sym)
    {
      if (string.Equals(sym, "#PCDATA", StringComparison.OrdinalIgnoreCase)) 
      {               
        _mixed = true;
      } 
      else 
      {
        _members.Add(sym);
      }
    }

    /// <summary>
    /// Adds a connector onto the member list.
    /// </summary>
    /// <param name="c">The connector character to add.</param>
    /// <exception cref="SgmlParseException">
    /// If the content is not mixed and has no members yet, or if the group type has been set and the
    /// connector does not match the group type.
    /// </exception>
    public void AddConnector(char c)
    {
      if (!_mixed && _members.Count == 0) 
      {
        throw new SgmlParseException(string.Format(CultureInfo.CurrentUICulture, "Missing token before connector '{0}'.", c));
      }

      var groupTypes = new Dictionary<char, GroupType> {
        {',', GroupType.Sequence},
        {'|', GroupType.Or},
        {'&', GroupType.And}
      };

      var gt = GroupType.None;
      if( groupTypes.ContainsKey( c )) {
        gt = groupTypes[ c ];
      }

      if (_groupType != GroupType.None && _groupType != gt) 
      {
        throw new SgmlParseException(string.Format(CultureInfo.CurrentUICulture, "Connector '{0}' is inconsistent with {1} group.", c, _groupType));
      }

      _groupType = gt;
    }

    /// <summary>
    /// Adds an occurrence character for this group, setting it's <see cref="Occurrence"/> value.
    /// </summary>
    /// <param name="c">The occurrence character.</param>
    public void AddOccurrence(char c)
    {
      var occurences = new Dictionary<char, Occurrence> {
        {'?', Occurrence.Optional},
        {'+', Occurrence.OneOrMore},
        {'*', Occurrence.ZeroOrMore}
      };

      if( occurences.ContainsKey( c )) {
        Occurrence = occurences[ c ];
        return;
      }

      Occurrence = Occurrence.Required;
    }

    /// <summary>
    /// Checks whether an element using this group can contain a specified element.
    /// </summary>
    /// <param name="name">The name of the element to look for.</param>
    /// <param name="dtd">The DTD to use during the checking.</param>
    /// <returns>true if an element using this group can contain the element, otherwise false.</returns>
    /// <remarks>
    /// Rough approximation - this is really assuming an "Or" group
    /// </remarks>
    public bool CanContain(string name, SgmlDtd dtd)
    {
      if (dtd == null)
        throw new ArgumentNullException("dtd");

      // Do a simple search of members.
      if( _members.OfType<string>().Any( s => s.Equals( name, StringComparison.OrdinalIgnoreCase ) ) ) {
        return true;
      }
      // didn't find it, so do a more expensive search over child elements
      // that have optional start tags and over child groups.
      foreach (var obj in _members) 
      {
        var s = obj as string;
        if (s != null)
        {
          var e = dtd.FindElement(s);
          if (e != null) 
          {
            if (e.StartTagOptional) 
            {
              // tricky case, the start tag is optional so element may be
              // allowed inside this guy!
              if (e.CanContain(name, dtd))
                return true;
            }
          }
        } 
        else 
        {
          var m = (Group)obj;
          if (m.CanContain(name, dtd)) 
            return true;
        }
      }

      return false;
    }
  }
}