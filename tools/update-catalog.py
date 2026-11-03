"""Refresh the bundled search catalog from public, downloadable official lists.
Python standard library only. Prices are fetched by the app, separately.
"""
import io
import json
from pathlib import Path
import re
import unicodedata
from urllib.request import Request, urlopen
import xml.etree.ElementTree as ET
import zipfile

SOURCES = {
    'stocks': 'https://www.jpx.co.jp/markets/statistics-equities/misc/tvdivq0000001vg2-att/data_j.xlsx',
    'funds': 'https://www.imaj.or.jp/find/nisa_growth_productslist/xlsx-cms/unlisted_fund_for_investor.xlsx',
    'mufg': 'https://www.am.mufg.jp/mukamapi/fund_search/?site_type=1',
}

def download(url):
    with urlopen(Request(url, headers={'User-Agent': 'Mozilla/5.0 OrbitCalendar catalog'}), timeout=45) as response:
        return response.read()

def rows(blob):
    ns = {'s': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
    with zipfile.ZipFile(io.BytesIO(blob)) as book:
        strings = [''.join(t.text or '' for t in si.findall('s:t', ns) + si.findall('s:r/s:t', ns))
                   for si in ET.fromstring(book.read('xl/sharedStrings.xml')).findall('s:si', ns)]
        result = []
        for row in ET.fromstring(book.read('xl/worksheets/sheet1.xml')).findall('.//s:row', ns):
            values = {}
            for cell in row.findall('s:c', ns):
                value = cell.find('s:v', ns)
                text = value.text if value is not None else ''
                values[re.sub('[0-9]', '', cell.attrib['r'])] = strings[int(text)] if text and cell.get('t') == 's' else text
            result.append(values)
        return result

def clean(value):
    return unicodedata.normalize('NFKC', str(value or '')).replace('\t', ' ').replace('\n', ' ').replace('\r', ' ').strip()

def build(stocks, funds, mufg):
    # id, name, short label, kind, code, aliases, provider, provider key, unit
    items = {}
    def add(row):
        row = [clean(x) for x in row]
        items[row[0]] = row
    for r in stocks[1:]:
        code, name = r.get('B', ''), r.get('C', '')
        if re.fullmatch('[0-9A-Z]{4}', code) and name:
            kind = 'ETF' if 'ETF' in r.get('D', '') else 'REIT' if 'REIT' in r.get('D', '') else '国内株'
            add(['t:'+code, name, name, kind, code, code+'.T', 'yahoo', code+'.T', '円'])
    for r in funds[2:]:
        code, name = r.get('C', ''), r.get('D', '')
        if re.fullmatch('[0-9A-Z]{8}', code) and name:
            add(['f:'+code, name, name, '投資信託', code, r.get('E', ''), '', '', '円'])
    for r in mufg:
        code = r.get('cfsd_association_fund_cd') or ''
        if not re.fullmatch('[0-9A-Z]{8}', code) or r.get('cfsd_redemption_flg') != 0:
            continue
        # Listed ETFs use the exchange feed, never a mutual fund NAV adapter.
        if r.get('cfsd_meigara_cd'):
            continue
        name = r['cfsd_fund_name']
        nick = r.get('cfsd_fund_nick_name') or ''
        aliases = ' '.join(str(r.get(k) or '') for k in ['cfsd_fund_name_kana', 'cfsd_fund_short_name',
                            'cfsd_synonym_list', 'cfsd_keyword', 'cfsd_isin_cd'])
        add(['f:'+code, name, nick or name, '投資信託', code, nick+' '+aliases, 'mufg', r['cfsd_fund_cd'], '円'])
    extras = [
        ['y:^N225','日経平均','日経平均','指数','^N225','日経225 Nikkei N225','yahoo','^N225','pt'],
        ['y:JPY=X','米ドル / 円','ドル円','為替','JPY=X','ドル円 USDJPY USD/JPY ドルえん','yahoo','JPY=X','円/ドル'],
        ['y:^GSPC','S&P 500','S&P 500','指数','^GSPC','SP500 S&P500 エスアンドピー500','yahoo','^GSPC','pt'],
        ['y:^DJI','ダウ平均','ダウ平均','指数','^DJI','NYダウ Dow','yahoo','^DJI','pt'],
        ['y:^IXIC','NASDAQ総合','NASDAQ','指数','^IXIC','ナスダック Nasdaq','yahoo','^IXIC','pt'],
    ]
    for code, name in [('AAPL','アップル'),('MSFT','マイクロソフト'),('NVDA','エヌビディア'),('AMZN','アマゾン'),
                       ('GOOGL','アルファベット'),('META','メタ'),('TSLA','テスラ'),('VT','全世界株式ETF'),
                       ('VOO','S&P 500 ETF'),('VTI','全米株式ETF'),('QQQ','NASDAQ 100 ETF')]:
        extras.append(['y:'+code,name,code,'米国株・ETF',code,code,'yahoo',code,'USD'])
    for r in extras:
        add(r)
    for r in items.values():
        if r[4] == '0331418A': r[2] = 'オルカン'
        if r[4] == '03311187': r[2] = 'eMAXIS Slim S&P500'
        if r[4] == '7203': r[2] = 'トヨタ'; r[5] += ' トヨタ Toyota'
        if r[4] == '6758': r[2] = 'ソニー'; r[5] += ' ソニー Sony'
        if 'SBI・全世界株式' in r[1]: r[5] += ' 雪だるま'
    return list(items.values())

if __name__ == '__main__':
    from datetime import date
    data = build(rows(download(SOURCES['stocks'])), rows(download(SOURCES['funds'])),
                 json.loads(download(SOURCES['mufg']))['datasets']['api00001tmCmFndSearchDetailOutDto'])
    target = Path(__file__).resolve().parents[1] / 'src' / 'instruments.tsv'
    target.write_text('# Snapshot '+str(date.today())+'; see docs/market-catalog.md\n' +
                      '\n'.join('\t'.join(r) for r in data)+'\n', encoding='utf-8')
    print('Catalog:',len(data),'instruments;',sum(r[3]=='投資信託' for r in data),'funds')
