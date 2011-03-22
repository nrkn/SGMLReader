using System;
using System.Collections.Generic;
using System.Linq;
using SgmlReaderDll.Parser.Extensions;

namespace SgmlReaderDll.Parser {
  /// <summary>
  /// An element declaration in a DTD.
  /// </summary>
  public class ElementDecl
  {
    private readonly string[] _inclusions;
    private readonly string[] _exclusions;
    private Dictionary<string, AttDef> _attList;

    /// <summary>
    /// Initialises a new element declaration instance.
    /// </summary>
    /// <param name="name">The name of the element.</param>
    /// <param name="sto">Whether the start tag is optional.</param>
    /// <param name="eto">Whether the end tag is optional.</param>
    /// <param name="cm">The <see cref="ContentModel"/> of the element.</param>
    /// <param name="inclusions"></param>
    /// <param name="exclusions"></param>
    public ElementDecl(string name, bool sto, bool eto, ContentModel cm, string[] inclusions, string[] exclusions)
    {
      Name = name;
      StartTagOptional = sto;
      EndTagOptional = eto;
      ContentModel = cm;
      _inclusions = inclusions;
      _exclusions = exclusions;
    }

    /// <summary>
    /// The element name.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// The <see cref="Parser.ContentModel"/> of the element declaration.
    /// </summary>
    public ContentModel ContentModel { get; private set; }

    /// <summary>
    /// Whether the end tag of the element is optional.
    /// </summary>
    /// <value>true if the end tag of the element is optional, otherwise false.</value>
    public bool EndTagOptional { get; private set; }

    /// <summary>
    /// Whether the start tag of the element is optional.
    /// </summary>
    /// <value>true if the start tag of the element is optional, otherwise false.</value>
    public bool StartTagOptional { get; private set; }

    /// <summary>
    /// Finds the attribute definition with the specified name.
    /// </summary>
    /// <param name="name">The name of the <see cref="AttDef"/> to find.</param>
    /// <returns>The <see cref="AttDef"/> with the specified name.</returns>
    /// <exception cref="InvalidOperationException">If the attribute list has not yet been initialised.</exception>
    public AttDef FindAttribute(string name)
    {
      if (_attList == null)
        throw new InvalidOperationException("The attribute list for the element declaration has not been initialised.");

      AttDef a;
      _attList.TryGetValue(name.ToUpperInvariant(), out a);
      return a;
    }

    /// <summary>
    /// Adds attribute definitions to the element declaration.
    /// </summary>
    /// <param name="list">The list of attribute definitions to add.</param>
    public void AddAttDefs(Dictionary<string, AttDef> list)
    {
      if (list == null)
        throw new ArgumentNullException("list");

      if (_attList == null) 
      {
        _attList = list;
      } 
      else 
      {
        foreach( var a in list.Values.Where( a => !_attList.ContainsKey( a.Name ) ) ) {
          _attList.Add(a.Name, a);
        }
      }
    }

    /// <summary>
    /// Tests whether this element can contain another specified element.
    /// </summary>
    /// <param name="name">The name of the element to check for.</param>
    /// <param name="dtd">The DTD to use to do the check.</param>
    /// <returns>True if the specified element can be contained by this element.</returns>
    public bool CanContain(string name, SgmlDtd dtd)
    {
      if( _exclusions.ContainsCaseInvariant( name ) ) return false;

      return _inclusions.ContainsCaseInvariant( name ) || ContentModel.CanContain(name, dtd);
    }
  }
}