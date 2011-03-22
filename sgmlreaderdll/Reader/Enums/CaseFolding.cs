﻿namespace SgmlReaderDll.Reader.Enums {
  /// <summary>
  /// SGML is case insensitive, so here you can choose between converting
  /// to lower case or upper case tags.  "None" means that the case is left
  /// alone, except that end tags will be folded to match the start tags.
  /// </summary>
  public enum CaseFolding {
    /// <summary>
    /// Do not convert case, except for converting end tags to match start tags.
    /// </summary>
    None,

    /// <summary>
    /// Convert tags to upper case.
    /// </summary>
    ToUpper,

    /// <summary>
    /// Convert tags to lower case.
    /// </summary>
    ToLower
  }
}