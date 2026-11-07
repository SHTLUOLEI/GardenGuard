"""从归档原字体生成覆盖当前工程文本的候选子集；不写Unity Assets。"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import zipfile

from fontTools import subset
from fontTools.ttLib import TTFont
from openpyxl import load_workbook


def corpus(root):
    points = set(range(0x20, 0x7f)) | {0xa0, 0x2026, 0x3000, 0xfffd}
    inputs = []
    folders = [root / 'GameUnity/Assets/Scripts', root / 'GameUnity/Assets/DGame.AOT',
               root / 'GameUnity/Assets/DGame/Runtime', root / 'GameUnity/Assets/BundleAssets',
               root / 'GameUnity/Assets/Scenes', root / 'GameUnity/Configs/Json']
    for folder in folders:
        for path in sorted(folder.rglob('*')):
            if path.suffix.lower() not in {'.cs', '.prefab', '.unity', '.asset', '.json', '.txt'}:
                continue
            data = path.read_bytes()
            try:
                text = data.decode('utf-8-sig')
            except UnicodeDecodeError:
                continue
            if '\0' in text:
                continue
            text = re.sub(r'\\u([0-9a-fA-F]{4})', lambda m: chr(int(m[1], 16)), text)
            points.update(map(ord, text))
            inputs.append({'path': path.relative_to(root).as_posix(),
                           'sha256': hashlib.sha256(data).hexdigest()})
    for path in sorted((root / 'GameConfig/Datas').rglob('*.xlsx')):
        if path.name.startswith('~$'):
            continue
        book = load_workbook(path, read_only=True, data_only=False)
        try:
            for sheet in book:
                for row in sheet.iter_rows(values_only=True):
                    for value in row:
                        if isinstance(value, str):
                            points.update(map(ord, value))
        finally:
            book.close()
        inputs.append({'path': path.relative_to(root).as_posix(),
                       'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
    return points, inputs


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-archive', required=True, type=Path, help='包含完整原字体的BASELINE.zip')
    parser.add_argument('--output', required=True, type=Path, help='Unity Assets以外的候选输出目录')
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    output = args.output.resolve()
    if output.is_relative_to(root / 'GameUnity/Assets'):
        parser.error('候选输出必须位于Unity Assets之外；通过Unity import_asset导入')
    output.mkdir(parents=True, exist_ok=True)
    points, inputs = corpus(root)
    report = {'inputs': inputs, 'codepoints': sorted(points), 'fonts': []}
    with zipfile.ZipFile(args.source_archive) as archive:
        for name in ['SmileySans.ttf', 'ZCOOLKuaiLe-Regular.ttf']:
            path = 'GameUnity/Assets/BundleAssets/Fonts/' + name
            data = archive.read(path)
            font = TTFont(io.BytesIO(data))
            original = font.getBestCmap()
            needed = points & set(original)
            options = subset.Options()
            options.name_IDs = ['*']
            options.name_languages = ['*']
            options.name_legacy = True
            options.recalc_timestamp = False
            worker = subset.Subsetter(options=options)
            worker.populate(unicodes=needed)
            worker.subset(font)
            target = output / name
            font.save(target)
            with TTFont(target) as saved:
                missing = needed - set(saved.getBestCmap())
            if missing:
                raise ValueError(f'{name}: missing codepoints {sorted(missing)}')
            report['fonts'].append({'asset': path, 'originalBytes': len(data),
                'subsetBytes': target.stat().st_size, 'originalGlyphs': len(original),
                'retainedCodepoints': sorted(needed), 'missingAfterSubset': sorted(missing),
                'sha256': hashlib.sha256(target.read_bytes()).hexdigest()})
            print(f'{name}: {len(data)} -> {target.stat().st_size} bytes; {len(needed)} codepoints; missing=0')
    (output / 'FONT_COVERAGE.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')


if __name__ == '__main__':
    main()
