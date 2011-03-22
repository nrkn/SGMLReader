/*
 * 
 * Copyright (c) 2007-2011 MindTouch. All rights reserved.
 * 
 */

using System;
using System.IO;
using System.Text;
using System.Xml;
using SgmlReaderDll.Reader.Enums;

namespace SgmlReader {
  /// <summary>
  /// This class provides a command line interface to the SgmlReader.
  /// </summary>
  public class CommandLine {
    string _proxy;
    string _output;
    bool _formatted;
    bool _noxmldecl;
    Encoding _encoding;

    [STAThread]
    static void Main( string[] args ) {
      try {
        var commandLine = new CommandLine();
        commandLine.Run( args );
      }
      catch( Exception e ) {
        Console.WriteLine( "Error: " + e.Message );
      }
      return;
    }

    public void Run( string[] args ) {
      var reader = new SgmlReaderDll.Reader.SgmlReader();
      string inputUri = null;

      for( var i = 0; i < args.Length; i++ ) {
        var arg = args[ i ];
        if( arg[ 0 ] == '-' || arg[ 0 ] == '/' ) {
          switch( arg.Substring( 1 ) ) {
            case "e":
              var errorlog = args[ ++i ];
              if( errorlog.ToLower() == "$stderr" ) {
                reader.ErrorLog = Console.Error;
              }
              else {
                reader.ErrorLogFile = errorlog;
              }
              break;
            case "html":
              reader.DocType = "HTML";
              break;
            case "dtd":
              reader.SystemLiteral = args[ ++i ];
              break;
            case "proxy":
              _proxy = args[ ++i ];
              reader.WebProxy = _proxy;
              break;
            case "encoding":
              _encoding = Encoding.GetEncoding( args[ ++i ] );
              break;
            case "f":
              _formatted = true;
              reader.WhitespaceHandling = WhitespaceHandling.None;
              break;
            case "noxml":
              _noxmldecl = true;
              break;
            case "doctype":
              reader.StripDocType = false;
              break;
            case "lower":
              reader.CaseFolding = CaseFolding.ToLower;
              break;
            case "upper":
              reader.CaseFolding = CaseFolding.ToUpper;
              break;

            default:
              Console.WriteLine( "Usage: SgmlReader <options> [InputUri] [OutputFile]" );
              Console.WriteLine( "-e log         Optional log file name, name of '$STDERR' will write errors to stderr" );
              Console.WriteLine( "-f             Whether to pretty print the output." );
              Console.WriteLine( "-html          Specify the built in HTML dtd" );
              Console.WriteLine( "-dtd url       Specify other SGML dtd to use" );
              Console.WriteLine( "-base          Add base tag to output HTML" );
              Console.WriteLine( "-noxml         Do not add XML declaration to the output" );
              Console.WriteLine( "-proxy svr:80  Proxy server to use for http requests" );
              Console.WriteLine( "-encoding name Specify an encoding for the output file (default UTF-8)" );
              Console.WriteLine( "-lower         Convert input tags to lower case" );
              Console.WriteLine( "-upper         Convert input tags to upper case" );
              Console.WriteLine();
              Console.WriteLine( "InputUri       The input file or http URL (default stdin).  " );
              Console.WriteLine( "               Supports wildcards for local file names." );
              Console.WriteLine( "OutputFile     Output file name (default stdout)" );
              Console.WriteLine( "               If input file contains wildcards then this just specifies the output file extension (default .xml)" );
              return;
          }
        }
        else {
          if( inputUri == null ) {
            inputUri = arg;
            var ext = Path.GetExtension( arg ).ToLower();
            if( ext == ".htm" || ext == ".html" )
              reader.DocType = "HTML";
          }
          else if( _output == null ) _output = arg;
        }
      }
      if( inputUri != null && !inputUri.StartsWith( "http://" ) && inputUri.IndexOfAny( new[] { '*', '?' } ) >= 0 ) {
        // wild card processing of a directory of files.
        var path = Path.GetDirectoryName( inputUri );
        if( path == "" ) path = ".\\";
        var ext = ".xml";
        if( _output != null )
          ext = Path.GetExtension( _output );
        foreach( var uri in Directory.GetFiles( path, Path.GetFileName( inputUri ) ) ) {
          Console.WriteLine( "Processing: " + uri );
          var file = Path.GetFileName( uri );
          _output = Path.GetDirectoryName( uri ) + Path.DirectorySeparatorChar + Path.GetFileNameWithoutExtension( file ) + ext;
          Process( reader, uri );
          reader.Close();
        }
        return;
      }
      Process( reader, inputUri );
      reader.Close();

      return;
    }

    void Process( SgmlReaderDll.Reader.SgmlReader reader, string uri ) {
      if( uri == null ) {
        reader.InputStream = Console.In;
      }
      else {
        reader.Href = uri;
      }


      if( _encoding == null ) {
        _encoding = reader.GetEncoding();
      }

      var w = _output != null ? new XmlTextWriter( _output, _encoding ) : new XmlTextWriter( Console.Out );

      if( _formatted ) w.Formatting = Formatting.Indented;
      if( !_noxmldecl ) {
        w.WriteStartDocument();
      }
      reader.Read();
      while( !reader.EOF ) {
        w.WriteNode( reader, true );
      }
      w.Flush();
      w.Close();
    }
  }
}