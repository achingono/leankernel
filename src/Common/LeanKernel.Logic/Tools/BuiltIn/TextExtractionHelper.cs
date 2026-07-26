namespace LeanKernel.Logic.Tools.BuiltIn;

/// <summary>
/// Extracts readable text from various file types including text files, EPUB, DOCX, PPTX, and OCR candidates.
/// </summary>
public static class TextExtractionHelper
{
    /// <summary>
    /// Extracts readable text from the specified file.
    /// </summary>
    /// <param name="path">The file path to extract text from.</param>
    /// <param name="scratchRoot">The scratch directory root for temporary files.</param>
    /// <param name="pythonExecutable">The Python executable path for OCR/extraction scripts.</param>
    /// <param name="maxExtractedCharacters">The maximum number of characters to extract.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The extracted text content.</returns>
    public static async Task<string> ExtractAsync(string path, string scratchRoot, string pythonExecutable, int maxExtractedCharacters, CancellationToken ct)
    {
        if (FileSystemSupport.IsTextLikeExtension(path))
        {
            var text = await File.ReadAllTextAsync(path, ct);
            return Truncate(text, maxExtractedCharacters);
        }

        if (FileSystemSupport.IsEpubCandidate(path))
        {
            var epubScript = """
import json, sys, zipfile, html.parser, os, re, xml.etree.ElementTree as ET
from pathlib import Path

path = Path(sys.argv[1])
extracted = []

with zipfile.ZipFile(path) as zf:
    ns = {'c': 'urn:oasis:names:tc:opendocument:xmlns:container'}
    container = ET.fromstring(zf.read('META-INF/container.xml'))
    rootfile = container.find('.//c:rootfile', ns)
    if rootfile is None:
        raise SystemExit('No rootfile found in container.xml')
    opf_path = rootfile.get('full-path', '')
    opf_dir = os.path.dirname(opf_path)
    opf_xml = ET.fromstring(zf.read(opf_path))
    opf_ns = opf_xml.tag.split('}')[0].strip('{') if '}' in opf_xml.tag else ''
    ns2 = {'p': opf_ns} if opf_ns else {}
    manifest = {}
    for item_elem in opf_xml.iter('{http://www.idpf.org/2007/opf}item') if opf_ns else opf_xml.iter('item'):
        item_id = item_elem.get('id', '')
        item_href = item_elem.get('href', '')
        item_media = (item_elem.get('media-type', '') or '').lower()
        if item_id and item_href:
            manifest[item_id] = {'href': item_href, 'media': item_media}
    spine_items = []
    for ref in (opf_xml.iter('{http://www.idpf.org/2007/opf}itemref') if opf_ns else opf_xml.iter('itemref')):
        ref_id = ref.get('idref', '')
        if ref_id in manifest:
            spine_items.append(manifest[ref_id])
    text_content = []
    for item in spine_items:
        href = item['href']
        media = item['media']
        if 'xhtml' not in media and 'html' not in media and 'xml' not in media:
            continue
        full_path = os.path.normpath(os.path.join(opf_dir, href))
        try:
            content = zf.read(full_path).decode('utf-8', errors='replace')
        except KeyError:
            continue
        class MLStripper(html.parser.HTMLParser):
            def __init__(self):
                super().__init__()
                self.reset()
                self.strict = False
                self.convert_charrefs = True
                self.text = []
            def handle_data(self, d):
                self.text.append(d)
        s = MLStripper()
        s.feed(re.sub(r'<(script|style)[^>]*>.*?</\1>', '', content, flags=re.DOTALL))
        text = ''.join(s.text).strip()
        if text:
            text_content.append(text)
    extracted = '\n\n'.join(text_content) if text_content else ''
print(extracted if extracted else '[No extractable text content found in EPUB]')
""";
            var epubOutput = await FileSystemSupport.RunPythonAsync(pythonExecutable, epubScript, [path], ct);
            return Truncate(epubOutput, maxExtractedCharacters);
        }

        if (FileSystemSupport.IsWordOpenXmlCandidate(path))
        {
            var docxScript = """
import sys, zipfile, xml.etree.ElementTree as ET
from pathlib import Path

path = Path(sys.argv[1])
ns = {'w': 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'}
text_parts = []
with zipfile.ZipFile(path) as zf:
    tree = ET.fromstring(zf.read('word/document.xml'))
    for t in tree.iter('{http://schemas.openxmlformats.org/wordprocessingml/2006/main}t'):
        if t.text:
            text_parts.append(t.text)
print('\n'.join(text_parts))
""";
            var docxOutput = await FileSystemSupport.RunPythonAsync(pythonExecutable, docxScript, [path], ct);
            return Truncate(docxOutput, maxExtractedCharacters);
        }

        if (FileSystemSupport.IsSpreadsheetOpenXmlCandidate(path))
        {
            var xlsxScript = """
import sys, zipfile, xml.etree.ElementTree as ET, re
from pathlib import Path

path = Path(sys.argv[1])
ns = {'s': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
shared_strings = []
rows = []

with zipfile.ZipFile(path) as zf:
    try:
        sst = ET.fromstring(zf.read('xl/sharedStrings.xml'))
        for si in sst.findall('.//s:si', ns):
            parts = [t.text or '' for t in si.findall('.//s:t', ns)]
            shared_strings.append(''.join(parts))
    except KeyError:
        pass

    sheet_names = [name for name in zf.namelist() if re.match(r'xl/worksheets/sheet\d+\.xml', name)]
    for sheet_name in sorted(sheet_names):
        sheet = ET.fromstring(zf.read(sheet_name))
        for row in sheet.findall('.//s:row', ns):
            cells = []
            for cell in row.findall('s:c', ns):
                cell_type = cell.get('t')
                value = ''
                if cell_type == 's':
                    v = cell.find('s:v', ns)
                    if v is not None and v.text and v.text.isdigit():
                        idx = int(v.text)
                        if 0 <= idx < len(shared_strings):
                            value = shared_strings[idx]
                elif cell_type == 'inlineStr':
                    is_node = cell.find('s:is', ns)
                    if is_node is not None:
                        value = ''.join((t.text or '') for t in is_node.findall('.//s:t', ns))
                else:
                    v = cell.find('s:v', ns)
                    if v is not None and v.text:
                        value = v.text

                if value:
                    cells.append(value)

            if cells:
                rows.append('\t'.join(cells))

print('\n'.join(rows))
""";
            var xlsxOutput = await FileSystemSupport.RunPythonAsync(pythonExecutable, xlsxScript, [path], ct);
            return Truncate(xlsxOutput, maxExtractedCharacters);
        }

        if (FileSystemSupport.IsPresentationOpenXmlCandidate(path))
        {
            var pptxScript = """
import sys, zipfile, xml.etree.ElementTree as ET, re
from pathlib import Path

path = Path(sys.argv[1])
ns = {'a': 'http://schemas.openxmlformats.org/drawingml/2006/main'}
text_parts = []
with zipfile.ZipFile(path) as zf:
    for name in zf.namelist():
        if re.match(r'ppt/slides/slide\d+\.xml', name):
            tree = ET.fromstring(zf.read(name))
            for t in tree.iter('{http://schemas.openxmlformats.org/drawingml/2006/main}t'):
                if t.text:
                    text_parts.append(t.text)
            text_parts.append('')
print('\n'.join(text_parts))
""";
            var pptxOutput = await FileSystemSupport.RunPythonAsync(pythonExecutable, pptxScript, [path], ct);
            return Truncate(pptxOutput, maxExtractedCharacters);
        }

        if (FileSystemSupport.IsLegacyOfficeBinaryCandidate(path))
        {
            var binaryText = await ExtractLegacyOfficeBinaryTextAsync(path, ct);
            return Truncate(binaryText, maxExtractedCharacters);
        }

        if (!FileSystemSupport.IsOcrCandidate(path))
        {
            throw new InvalidOperationException($"Unsupported file type '{Path.GetExtension(path)}' for text extraction.");
        }

        var paddleOcrScript = """
import json
import sys
from pathlib import Path
from paddleocr import PaddleOCR
try:
    from pdf2image import convert_from_path
except Exception:
    convert_from_path = None
path = Path(sys.argv[1])
ocr = PaddleOCR(use_angle_cls=True, lang='en')
def collect_text(result):
    lines = []
    if not result:
        return lines
    for block in result:
        if not block:
            continue
        for item in block:
            if not item or len(item) < 2:
                continue
            text = item[1][0] if isinstance(item[1], (list, tuple)) and item[1] else ""
            if text:
                lines.append(text)
    return lines
if path.suffix.lower() == '.pdf':
    if convert_from_path is None:
        raise SystemExit('pdf2image is not available.')
    pages = convert_from_path(str(path))
    all_lines = []
    for page in pages:
        all_lines.extend(collect_text(ocr.ocr(page, cls=True)))
    print("\n".join(all_lines))
else:
    print("\n".join(collect_text(ocr.ocr(str(path), cls=True))))
""";

        try
        {
            var output = await FileSystemSupport.RunPythonAsync(pythonExecutable, paddleOcrScript, [path], ct);
            return Truncate(output, maxExtractedCharacters);
        }
        catch (InvalidOperationException paddleError)
        {
            var tesseractOcrScript = """
import sys
from pathlib import Path
import pytesseract
from PIL import Image
try:
    from pdf2image import convert_from_path
except Exception:
    convert_from_path = None
path = Path(sys.argv[1])
if path.suffix.lower() == '.pdf':
    if convert_from_path is None:
        raise SystemExit('pdf2image is not available.')
    pages = convert_from_path(str(path))
    chunks = []
    for page in pages:
        text = pytesseract.image_to_string(page)
        if text and text.strip():
            chunks.append(text.strip())
    print("\n\n".join(chunks))
else:
    with Image.open(path) as img:
        print(pytesseract.image_to_string(img))
""";

            try
            {
                var fallbackOutput = await FileSystemSupport.RunPythonAsync(pythonExecutable, tesseractOcrScript, [path], ct);
                return Truncate(fallbackOutput, maxExtractedCharacters);
            }
            catch (InvalidOperationException fallbackError)
            {
                throw new InvalidOperationException(
                    $"OCR extraction failed with PaddleOCR and Tesseract fallback. PaddleOCR error: {paddleError.Message} | Tesseract error: {fallbackError.Message}");
            }
        }
    }

    private static string Truncate(string value, int limit)
    {
        if (value.Length <= limit)
        {
            return value;
        }

        return value[..limit] + "\n\n[Content truncated to " + limit + " characters.]";
    }

    private static async Task<string> ExtractLegacyOfficeBinaryTextAsync(string path, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct);
        var fragments = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        CollectPrintableStrings(bytes, isUnicode: false, minLength: 3, fragments, seen);
        CollectPrintableStrings(bytes, isUnicode: true, minLength: 3, fragments, seen);

        if (fragments.Count == 0)
        {
            throw new InvalidOperationException(
                $"No extractable text found in legacy Office binary '{Path.GetExtension(path)}'.");
        }

        return string.Join("\n", fragments);
    }

    private static void CollectPrintableStrings(
        IReadOnlyList<byte> bytes,
        bool isUnicode,
        int minLength,
        ICollection<string> output,
        ISet<string> seen)
    {
        var chars = new List<char>();
        var step = isUnicode ? 2 : 1;

        for (var i = 0; i < bytes.Count; i += step)
        {
            char ch;
            if (isUnicode)
            {
                if (i + 1 >= bytes.Count)
                {
                    break;
                }

                if (bytes[i + 1] != 0)
                {
                    FlushBuffer(chars, minLength, output, seen);
                    continue;
                }

                ch = (char)bytes[i];
            }
            else
            {
                ch = (char)bytes[i];
            }

            if (ch is '\r' or '\n' or '\t')
            {
                ch = ' ';
            }

            if (char.IsLetterOrDigit(ch) || char.IsPunctuation(ch) || ch == ' ')
            {
                chars.Add(ch);
                continue;
            }

            FlushBuffer(chars, minLength, output, seen);
        }

        FlushBuffer(chars, minLength, output, seen);
    }

    private static void FlushBuffer(
        List<char> chars,
        int minLength,
        ICollection<string> output,
        ISet<string> seen)
    {
        if (chars.Count < minLength)
        {
            chars.Clear();
            return;
        }

        var text = new string(chars.ToArray()).Trim();
        chars.Clear();

        if (text.Length < minLength)
        {
            return;
        }

        if (seen.Add(text))
        {
            output.Add(text);
        }
    }
}
