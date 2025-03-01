﻿using System;
using System.Collections.Generic;
using System.IO;
using Compression.FileHandling;
using Intervals;
using Jasix.DataStructures;
using Newtonsoft.Json;
using OptimizedCore;
using Utilities = Jasix.DataStructures.Utilities;

namespace Jasix;

public sealed class QueryProcessor : IDisposable
{
    private static readonly byte[] BgzBlock = new byte[BlockGZipStream.BlockGZipFormatCommon.MaxBlockSize];

    public QueryProcessor(StreamReader jsonReader, Stream indexStream, StreamWriter writer = null, string[] includeFields = null)
    {
        _jsonReader    = jsonReader;
        _writer        = writer ?? new StreamWriter(Console.OpenStandardOutput());
        _indexStream   = indexStream;
        _jasixIndex    = new JasixIndex(_indexStream);
        _includeFields = includeFields;
    }

    #region IDisposable

    public void Dispose()
    {
        _jsonReader?.Dispose();
        _writer?.Dispose();
        _indexStream?.Dispose();
    }

    #endregion


    public void ListChromosomesAndSections()
    {
        foreach (string chrName in _jasixIndex.GetChromosomeList()) _writer.WriteLine(chrName);

        foreach (string section in _jasixIndex.GetSections()) _writer.WriteLine(section);
    }

    public void PrintHeaderOnly()
    {
        string headerString = "{" + GetHeader() + "}";
        Utilities.PrintJsonEntry(headerString, false, _writer);
    }

    public void PrintSection(string section)
    {
        _writer.WriteLine("[");
        var needComma = false;
        foreach (string line in GetSectionLines(section))
        {
            Utilities.PrintJsonEntry(line.TrimEnd(','), needComma, _writer);
            needComma = true;
        }

        _writer.WriteLine("]");
    }

    public int ProcessQuery(IEnumerable<string> queryStrings, bool printHeader = false)
    {
        if (printHeader)
        {
            _writer.Write("{\n\"header\":");
            string headerContent = GetHeader().Split(':', 2)[1];
            Utilities.PrintJsonEntry(headerContent, false, _writer);
            _writer.WriteLine(",");
        }
        else
        {
            _writer.Write("{");
        }

        Utilities.PrintQuerySectionOpening(JasixCommons.PositionsSectionTag, _writer);

        var count = 0;
        foreach (string queryString in queryStrings)
        {
            (string Chromosome, int Start, int End) query = Utilities.ParseQuery(queryString);
            query.Chromosome = _jasixIndex.GetIndexChromName(query.Chromosome);
            if (!_jasixIndex.ContainsChr(query.Chromosome)) continue;

            count += PrintLargeVariantsExtendingIntoQuery(query);
            count += PrintAllVariantsFromQueryBegin(query, count > 0);
        }

        Utilities.PrintQuerySectionClosing(_writer);
        _writer.WriteLine("}");
        return count;
    }

    private int PrintAllVariantsFromQueryBegin((string, int, int) query, bool needComma)
    {
        var count = 0;
        foreach (string line in ReadOverlappingJsonLines(query))
        {
            Utilities.PrintJsonEntry(line, needComma, _writer);
            needComma = true;
            count++;
        }

        return count;
    }

    private int PrintLargeVariantsExtendingIntoQuery((string, int, int) query)
    {
        var count = 0;
        foreach (string line in ReadJsonLinesExtendingInto(query))
        {
            Utilities.PrintJsonEntry(line, count > 0, _writer);
            count++;
        }

        return count;
    }

    internal IEnumerable<string> ReadJsonLinesExtendingInto((string Chr, int Start, int End) query)
    {
        // query for large variants like chr1:100-99 returns all overlapping large variants that start before 100
        (string chr, int start, _) = query;
        long[] locations = _jasixIndex.LargeVariantPositions(chr, start, start - 1);

        if (locations == null || locations.Length == 0) yield break;

        foreach (long location in locations)
        {
            RepositionReader(location);
            string line;
            while ((line = _jsonReader.ReadLine()) != null)
                if (!line.OptimizedStartsWith(','))
                {
                    //buffer starts with ',\n', skip this first line
                    line = line.TrimEnd(',');
                    yield return line;
                    break;
                }
        }
    }

    private void RepositionReader(long location)
    {
        _jsonReader.DiscardBufferedData();
        _jsonReader.BaseStream.Position = location;
    }

    public string GetHeader()
    {
        long headerLocation = _jasixIndex.GetSectionBegin(JasixCommons.HeaderSectionTag);
        RepositionReader(headerLocation);

        string headerLine     = _jsonReader.ReadLine();
        var    additionalTail = $",\"{JasixCommons.PositionsSectionTag}\":[";

        return headerLine?.Substring(1, headerLine.Length - 1 - additionalTail.Length);
    }

    public IEnumerable<string> GetSectionLines(string section)
    {
        if (_jasixIndex.GetSectionBegin(section) == -1) yield break;

        long sectionBegin = _jasixIndex.GetSectionBegin(section);
        RepositionReader(sectionBegin);

        string line = _jsonReader.ReadLine();
        // at the end of both positions and genes section, we have a line that closes the array.
        // So, our terminating condition can be the following
        while (line != null && !line.StartsWith("]"))
        {
            yield return line;
            line = _jsonReader.ReadLine();
        }
    }

    private static bool HasField(string line, IEnumerable<string> includeFields)
    {
        foreach (string field in includeFields)
            if (line.Contains("\"" + field + "\""))
                return true;
        return false;
    }

    internal IEnumerable<string> ReadOverlappingJsonLines((string Chr, int Start, int End) query)
    {
        (string chr, int start, int end) = query;
        long position = _jasixIndex.GetFirstVariantPosition(chr, start, end);

        if (position == -1) yield break;

        RepositionReader(position);

        string   line;
        string[] includeFields = { };

        if (_includeFields != null) includeFields = _includeFields;

        while ((line = _jsonReader.ReadLine()) != null && !line.OptimizedStartsWith(']'))
            //The array of positions entry end with "]," Going past it will cause the json deserializer to crash
        {
            line = line.TrimEnd(',');
            if (string.IsNullOrEmpty(line)) continue;
            
            if (_includeFields != null && !HasField(line, includeFields))
                continue;

            JsonSchema jsonEntry = ParseJsonEntry(line);

            string jsonChrom = _jasixIndex.GetIndexChromName(jsonEntry.chromosome);
            if (jsonChrom != chr) break;

            if (jsonEntry.Start > end) break;

            if (!jsonEntry.Overlaps(start, end)) continue;
            // if there is an SV that starts before the query start that is printed by the large variant printer
            if (Utilities.IsLargeVariant(jsonEntry.Start, jsonEntry.End) && jsonEntry.Start < start) continue;
            yield return line;
        }
    }

    private static JsonSchema ParseJsonEntry(string line)
    {
        JsonSchema jsonEntry;
        try
        {
            jsonEntry = JsonConvert.DeserializeObject<JsonSchema>(line);
        }
        catch (Exception)
        {
            Console.WriteLine($"Error in line:\n{line}");
            throw;
        }

        return jsonEntry;
    }

    #region members

    private readonly StreamReader _jsonReader;
    private readonly StreamWriter _writer;
    private readonly Stream       _indexStream;
    private readonly JasixIndex   _jasixIndex;
    private readonly string[]     _includeFields;

    #endregion
}