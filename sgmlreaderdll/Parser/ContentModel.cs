using System;
using System.Globalization;
using SgmlReaderDll.Parser.Enums;

namespace SgmlReaderDll.Parser {
  /// <summary>
  /// Defines the content model for an element.
  /// </summary>
  public class ContentModel
  {
    private Group _model;

    /// <summary>
    /// Initialises a new instance of the <see cref="ContentModel"/> class.
    /// </summary>
    public ContentModel()
    {
      _model = new Group(null);
    }

    /// <summary>
    /// The number of groups on the stack.
    /// </summary>
    public int CurrentDepth { get; private set; }

    /// <summary>
    /// The allowed child content, specifying if nested children are not allowed and if so, what content is allowed.
    /// </summary>
    public DeclaredContent DeclaredContent { get; private set; }

    /// <summary>
    /// Begins processing of a nested model group.
    /// </summary>
    public void PushGroup()
    {
      _model = new Group(_model);
      CurrentDepth++;
    }

    /// <summary>
    /// Finishes processing of a nested model group.
    /// </summary>
    /// <returns>The current depth of the group nesting, or -1 if there are no more groups to pop.</returns>
    public int PopGroup()
    {
      if (CurrentDepth == 0)
        return -1;

      CurrentDepth--;
      _model.Parent.AddGroup(_model);
      _model = _model.Parent;
      return CurrentDepth;
    }

    /// <summary>
    /// Adds a new symbol to the current group's members.
    /// </summary>
    /// <param name="sym">The symbol to add.</param>
    public void AddSymbol(string sym)
    {
      _model.AddSymbol(sym);
    }

    /// <summary>
    /// Adds a connector onto the member list for the current group.
    /// </summary>
    /// <param name="c">The connector character to add.</param>
    /// <exception cref="SgmlParseException">
    /// If the content is not mixed and has no members yet, or if the group type has been set and the
    /// connector does not match the group type.
    /// </exception>
    public void AddConnector(char c)
    {
      _model.AddConnector(c);
    }

    /// <summary>
    /// Adds an occurrence character for the current model group, setting it's <see cref="Occurrence"/> value.
    /// </summary>
    /// <param name="c">The occurrence character.</param>
    public void AddOccurrence(char c)
    {
      _model.AddOccurrence(c);
    }

    /// <summary>
    /// Sets the contained content for the content model.
    /// </summary>
    /// <param name="dc">The text specified the permissible declared child content.</param>
    public void SetDeclaredContent( string dc ) {
      // TODO: Validate that this can never combine with nexted groups?
      DeclaredContent declaredContent;
      if( !Enum.TryParse( dc, true, out declaredContent )) {
        throw new SgmlParseException( string.Format( CultureInfo.CurrentUICulture, "Declared content type '{0}' is not supported", dc ) );
      }
      DeclaredContent = declaredContent;
    }

    /// <summary>
    /// Checks whether an element using this group can contain a specified element.
    /// </summary>
    /// <param name="name">The name of the element to look for.</param>
    /// <param name="dtd">The DTD to use during the checking.</param>
    /// <returns>true if an element using this group can contain the element, otherwise false.</returns>
    public bool CanContain(string name, SgmlDtd dtd) {
      return DeclaredContent == DeclaredContent.Default && _model.CanContain(name, dtd);
    }
  }
}