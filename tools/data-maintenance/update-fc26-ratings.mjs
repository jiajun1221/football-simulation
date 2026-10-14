import fs from 'node:fs';
import path from 'node:path';

const writeChanges = process.argv.includes('--write');

const baseUrl = 'https://www.ea.com';
const dataDir = 'src/FootballSimulation.Core/Data/Json';
const sourceFiles = [
  'premier-league-2025-26-squads.json',
  'bundesliga-2025-26-squads.json',
  'la-liga-2025-26-squads.json',
  'ligue-1-2025-26-squads.json',
  'serie-a-2025-26-squads.json',
  'champions-league-2025-26-squads.json',
  'premier-league-2025-26-players.json'
];

const leagueUrls = [
  '/en/games/ea-sports-fc/ratings/leagues-ratings/premier-league/13',
  '/en/games/ea-sports-fc/ratings/leagues-ratings/bundesliga/19',
  '/en/games/ea-sports-fc/ratings/leagues-ratings/laliga-ea-sports/53',
  '/en/games/ea-sports-fc/ratings/leagues-ratings/serie-a-enilive/31',
  '/en/games/ea-sports-fc/ratings/leagues-ratings/ligue-1-mc-donald-s/16',
  '/en/games/ea-sports-fc/ratings/leagues-ratings/liga-portugal/308',
  '/en/games/ea-sports-fc/ratings/leagues-ratings/scottish-prem/50',
  '/en/games/ea-sports-fc/ratings/leagues-ratings/trendyol-super-lig/68',
  '/en/games/ea-sports-fc/ratings/leagues-ratings/1a-pro-league/4',
  '/en/games/ea-sports-fc/ratings/leagues-ratings/ceska-liga/319',
  '/en/games/ea-sports-fc/ratings/leagues-ratings/3f-superliga/1'
];

const teamAliases = {
  'brighton & hove albion': 'Brighton',
  'brighton and hove albion': 'Brighton',
  'manchester united': 'Man Utd',
  'newcastle united': 'Newcastle Utd',
  'nottingham forest': "Nott'm Forest",
  'tottenham hotspur': 'Spurs',
  'west ham united': 'West Ham',
  'wolverhampton wanderers': 'Wolves',
  'mainz 05': '1. FSV Mainz 05',
  'fc koln': '1. FC Koln',
  'heidenheim': 'Heidenheim',
  'bayern munich': 'FC Bayern Munchen',
  'st pauli': 'FC St. Pauli',
  'hamburg': 'Hamburger SV',
  'bayer leverkusen': 'Leverkusen',
  'borussia monchengladbach': "M'gladbach",
  'eintracht frankfurt': 'Frankfurt',
  'augsburg': 'FC Augsburg',
  'freiburg': 'SC Freiburg',
  'werder bremen': 'SV Werder Bremen',
  'hoffenheim': 'TSG Hoffenheim',
  'stuttgart': 'VfB Stuttgart',
  'wolfsburg': 'VfL Wolfsburg',
  'atletico madrid': 'Atletico de Madrid',
  'deportivo alaves': 'D. Alaves',
  'elche': 'Elche CF',
  'barcelona': 'FC Barcelona',
  'getafe': 'Getafe CF',
  'girona': 'Girona FC',
  'levante': 'Levante UD',
  'osasuna': 'CA Osasuna',
  'real oviedo': 'R. Oviedo',
  'celta vigo': 'Celta',
  'mallorca': 'RCD Mallorca',
  'espanyol': 'RCD Espanyol',
  'sevilla': 'Sevilla FC',
  'valencia': 'Valencia CF',
  'villarreal': 'Villarreal CF',
  'ac milan': 'Milano FC',
  'inter milan': 'Lombardia FC',
  'atalanta': 'Bergamo Calcio',
  'napoli': 'SSC Napoli',
  'lazio': 'Latium',
  'hellas verona': 'Hellas Verona',
  'monaco': 'AS Monaco',
  'lorient': 'FC Lorient',
  'le havre': 'Havre AC',
  'lille': 'LOSC Lille',
  'marseille': 'OM',
  'nice': 'OGC Nice',
  'lyon': 'OL',
  'paris saint-germain': 'Paris SG',
  'paris sg': 'Paris SG',
  'lens': 'RC Lens',
  'brest': 'Stade Brestois 29',
  'rennes': 'Stade Rennais FC',
  'toulouse': 'Toulouse FC',
  'nantes': 'FC Nantes',
  'auxerre': 'AJ Auxerre',
  'angers': 'Angers SCO',
  'metz': 'FC Metz',
  'benfica': 'SL Benfica',
  'porto': 'FC Porto',
  'salzburg': 'RB Salzburg',
  'young boys': 'BSC Young Boys',
  'sparta prague': 'Sparta Praha',
  'sturm graz': 'SK Sturm Graz',
  'red star belgrade': 'Crvena zvezda',
  'slovan bratislava': 'Slovan Bratislava'
};

const playerAliases = {
  'arsenal|gabriel': 'Gabriel Magalhaes',
  'arsenal|gabriel magalhaes': 'Gabriel Magalhaes',
  'arsenal|martin odegaard': 'Martin Odegaard',
  'aston villa|emiliano martinez': 'Emiliano Martinez',
  'aston villa|ollie watkins': 'Oliver Watkins',
  'liverpool|alisson becker': 'Alisson',
  'manchester city|ruben dias': 'Ruben Dias',
  'manchester city|savinho': 'Savio',
  'tottenham hotspur|son heung-min': 'Heung Min Son',
  'tottenham hotspur|heung-min son': 'Heung Min Son',
  'bayern munich|luis diaz': 'Luis Diaz',
  'real madrid|vinicius junior': 'Vini Jr.',
  'real madrid|vinicius jr': 'Vini Jr.',
  'paris saint-germain|ousmane dembele': 'Ousmane Dembele',
  'inter milan|lautaro martinez': 'Lautaro Martinez'
};

const specialLetters = {
  'ø': 'o',
  'Ø': 'O',
  'ð': 'd',
  'Ð': 'D',
  'þ': 'th',
  'Þ': 'Th',
  'ß': 'ss',
  'ł': 'l',
  'Ł': 'L',
  'đ': 'd',
  'Đ': 'D',
  'ı': 'i',
  'İ': 'I',
  'æ': 'ae',
  'Æ': 'Ae',
  'œ': 'oe',
  'Œ': 'Oe'
};

const ratingFields = [
  'overallRating',
  'pace',
  'shooting',
  'passing',
  'dribbling',
  'defending',
  'physical'
];

const teamAliasMap = createNormalizedMap(teamAliases);
const playerAliasMap = createNormalizedMap(playerAliases);

function createNormalizedMap(values) {
  return new Map(Object.entries(values).map(([key, value]) => [normalizeKey(key), value]));
}

function decodeHtml(value) {
  return String(value ?? '')
    .replace(/\\u([0-9a-fA-F]{4})/g, (_, hex) => String.fromCharCode(parseInt(hex, 16)))
    .replace(/&amp;/g, '&')
    .replace(/&#x27;/g, "'")
    .replace(/&quot;/g, '"')
    .replace(/&nbsp;/g, ' ');
}

function toAscii(value) {
  return decodeHtml(value)
    .replace(/[øØðÐþÞßłŁđĐıİæÆœŒ]/g, char => specialLetters[char] ?? char)
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '');
}

function normalizeKey(value) {
  return toAscii(value)
    .toLowerCase()
    .replace(/\b(fc|cf|afc|sc|sv|rcd|rc|as|sk|vfb|vfl|tsg|losc|ol|om|cd|ud)\b/g, '')
    .replace(/[^a-z0-9]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

function slugify(value) {
  return toAscii(value)
    .toLowerCase()
    .replace(/&/g, ' and ')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .replace(/-+/g, '-');
}

function stripTags(value) {
  return decodeHtml(String(value ?? '').replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ').trim());
}

function readAttribute(markup, name) {
  const match = String(markup ?? '').match(new RegExp(`${name}="([^"]*)"`));
  return match ? decodeHtml(match[1]) : '';
}

function readLastNumber(markup) {
  const numbers = stripTags(markup).match(/\d+/g);
  return numbers ? Number.parseInt(numbers.at(-1), 10) : null;
}

function parseRatingRows(html) {
  const rows = [];
  const parts = html.split('<tr class="Table_row__');

  for (const part of parts.slice(1)) {
    const row = `<tr class="Table_row__${part.split('</tr>')[0]}</tr>`;
    const cells = [...row.matchAll(/<td\b[^>]*>[\s\S]*?<\/td>/g)].map(match => match[0]);

    if (cells.length < 12) {
      continue;
    }

    const playerMatch = cells[1].match(/Table_profileLabel__[^>]*>([^<]+)/);
    const player = playerMatch ? decodeHtml(playerMatch[1]) : '';
    const team = readAttribute(cells[3].match(/<img [^>]*>/)?.[0] ?? '', 'alt');
    const values = cells.slice(5, 12).map(readLastNumber);

    if (!player || !team || values.some(value => value == null)) {
      continue;
    }

    rows.push({
      player,
      team,
      position: stripTags(cells[4]),
      overallRating: values[0],
      pace: values[1],
      shooting: values[2],
      passing: values[3],
      dribbling: values[4],
      defending: values[5],
      physical: values[6]
    });
  }

  return rows;
}

function getPlayersFromData(data) {
  const players = [];

  if (Array.isArray(data.teams)) {
    for (const team of data.teams) {
      for (const section of ['startingXI', 'substitutes']) {
        for (const player of team[section] ?? []) {
          players.push({ teamName: team.name, player });
        }
      }
    }
  } else if (Array.isArray(data.players)) {
    for (const player of data.players) {
      players.push({ teamName: (player.teamId ?? '').replace(/-/g, ' '), player });
    }
  }

  return players;
}

function resolveTeamName(localTeamName) {
  return teamAliasMap.get(normalizeKey(localTeamName)) ?? localTeamName;
}

function resolveTeamKey(localTeamName) {
  return normalizeKey(resolveTeamName(localTeamName));
}

function resolvePlayerKey(localTeamName, playerName) {
  const localKey = `${normalizeKey(localTeamName)}|${normalizeKey(playerName)}`;
  return normalizeKey(playerAliasMap.get(localKey) ?? playerName);
}

function updatePlayerFromRating(player, rating) {
  let changed = false;

  for (const field of ratingFields) {
    if (player[field] !== rating[field]) {
      player[field] = rating[field];
      changed = true;
    }
  }

  return changed;
}

async function fetchText(url) {
  const response = await fetch(url, { headers: { 'user-agent': 'Mozilla/5.0' } });
  return {
    status: response.status,
    text: await response.text(),
    url
  };
}

function addTeamHref(hrefByLabel, href, label) {
  hrefByLabel.set(normalizeKey(label), href.startsWith('http') ? href : `${baseUrl}${href}`);
}

function addTeamCandidate(teamsByLabel, id, label) {
  const key = normalizeKey(label);
  const candidates = teamsByLabel.get(key) ?? [];
  candidates.push({ id: Number(id), label });
  teamsByLabel.set(key, candidates);
}

async function buildTeamDirectory() {
  const hrefByLabel = new Map();
  const teamsByLabel = new Map();

  for (const relativeUrl of leagueUrls) {
    const { text } = await fetchText(`${baseUrl}${relativeUrl}`);

    for (const match of text.matchAll(/href="([^"]*\/teams-ratings\/[^"]+)"[\s\S]*?<img alt="([^"]+)"/g)) {
      addTeamHref(hrefByLabel, match[1], decodeHtml(match[2]));
    }

    for (const match of text.matchAll(/\{"id":(\d+),"label":"((?:[^"\\]|\\.)*)","imageUrl":"[^"]*\/l\d+\.png"/g)) {
      addTeamCandidate(teamsByLabel, match[1], decodeHtml(match[2]));
    }
  }

  return { hrefByLabel, teamsByLabel };
}

function collectLocalTeams() {
  const teams = new Map();

  for (const file of sourceFiles) {
    const data = JSON.parse(fs.readFileSync(path.join(dataDir, file), 'utf8'));

    for (const { teamName } of getPlayersFromData(data)) {
      teams.set(normalizeKey(teamName), teamName);
    }
  }

  return [...teams.values()];
}

function resolveTeamUrls(localTeams, directory) {
  const targetUrls = new Map();
  const unresolvedTeams = [];

  for (const localName of localTeams) {
    const eaName = resolveTeamName(localName);
    const teamKey = normalizeKey(eaName);
    const directHref = directory.hrefByLabel.get(teamKey);

    if (directHref) {
      targetUrls.set(localName, directHref);
      continue;
    }

    const candidates = (directory.teamsByLabel.get(teamKey) ?? [])
      .filter(candidate => candidate.id < 131000)
      .sort((left, right) => left.id - right.id);

    if (candidates.length > 0) {
      const candidate = candidates[0];
      targetUrls.set(
        localName,
        `${baseUrl}/en/games/ea-sports-fc/ratings/teams-ratings/${slugify(candidate.label)}/${candidate.id}`
      );
      continue;
    }

    unresolvedTeams.push({ localName, eaName });
  }

  return { targetUrls, unresolvedTeams };
}

async function fetchRatings(targetUrls) {
  const ratings = [];
  const failedPages = [];
  const uniqueUrls = new Map();

  for (const [localName, url] of targetUrls) {
    if (!uniqueUrls.has(url)) {
      uniqueUrls.set(url, []);
    }

    uniqueUrls.get(url).push(localName);
  }

  for (const [url, localNames] of uniqueUrls) {
    const { status, text } = await fetchText(url);
    const rows = status === 200 ? parseRatingRows(text) : [];

    if (rows.length === 0) {
      failedPages.push({ localNames, status, url });
    } else {
      ratings.push(...rows);
    }
  }

  return { ratings, failedPages, fetchedPages: uniqueUrls.size };
}

function buildRatingIndex(ratings) {
  const index = new Map();

  for (const rating of ratings) {
    const key = `${normalizeKey(rating.team)}|${normalizeKey(rating.player)}`;
    const existing = index.get(key) ?? [];

    if (!existing.some(item => JSON.stringify(item) === JSON.stringify(rating))) {
      existing.push(rating);
    }

    index.set(key, existing);
  }

  return index;
}

function updateDataFiles(ratingIndex) {
  const summary = [];
  const unmatched = [];
  const ambiguous = [];
  const samples = [];
  let total = 0;
  let matched = 0;
  let changed = 0;

  for (const file of sourceFiles) {
    const filePath = path.join(dataDir, file);
    const data = JSON.parse(fs.readFileSync(filePath, 'utf8'));
    const fileStats = {
      file,
      total: 0,
      matched: 0,
      changed: 0,
      ambiguous: 0,
      unmatched: 0
    };

    for (const item of getPlayersFromData(data)) {
      total += 1;
      fileStats.total += 1;

      const lookupKey = `${resolveTeamKey(item.teamName)}|${resolvePlayerKey(item.teamName, item.player.name)}`;
      const hits = ratingIndex.get(lookupKey) ?? [];

      if (hits.length === 1) {
        const before = item.player.overallRating;
        const didChange = updatePlayerFromRating(item.player, hits[0]);

        matched += 1;
        fileStats.matched += 1;

        if (didChange) {
          changed += 1;
          fileStats.changed += 1;

          if (samples.length < 25) {
            samples.push(`${item.teamName}: ${item.player.name} ${before} -> ${hits[0].overallRating}`);
          }
        }
      } else if (hits.length > 1) {
        fileStats.ambiguous += 1;
        ambiguous.push({ file, team: item.teamName, player: item.player.name });
      } else {
        fileStats.unmatched += 1;
        unmatched.push({ file, team: item.teamName, player: item.player.name });
      }
    }

    if (writeChanges) {
      fs.writeFileSync(filePath, `${JSON.stringify(data, null, 2)}\n`, 'utf8');
    }

    summary.push(fileStats);
  }

  return {
    summary,
    unmatched,
    ambiguous,
    samples,
    totals: {
      total,
      matched,
      changed,
      ambiguous: ambiguous.length,
      unmatched: unmatched.length
    }
  };
}

const directory = await buildTeamDirectory();
const localTeams = collectLocalTeams();
const { targetUrls, unresolvedTeams } = resolveTeamUrls(localTeams, directory);
const { ratings, failedPages, fetchedPages } = await fetchRatings(targetUrls);
const ratingIndex = buildRatingIndex(ratings);
const result = updateDataFiles(ratingIndex);

console.log(JSON.stringify({
  mode: writeChanges ? 'write' : 'dry-run',
  targetTeams: targetUrls.size,
  fetchedPages,
  failedPages,
  unresolvedTeams,
  ratingRows: ratings.length,
  uniqueRatingKeys: ratingIndex.size,
  files: result.summary,
  totals: result.totals,
  samples: result.samples,
  unmatchedSample: result.unmatched.slice(0, 120),
  ambiguousSample: result.ambiguous.slice(0, 120)
}, null, 2));
