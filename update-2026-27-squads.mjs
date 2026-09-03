import fs from 'node:fs';
import path from 'node:path';

const writeChanges = process.argv.includes('--write');
const baseUrl = 'https://www.ea.com';
const cutoff = '2026-09-02';
const season = '2026-27';
const dataDir = 'src/FootballSimulation.Core/Data/Json';
const officialLeagueSources = {
  'premier-league':'https://www.premierleague.com/en/clubs',
  'la-liga':'https://www.laliga.com/en-GB/laliga-easports/clubs',
  'bundesliga':'https://www.bundesliga.com/en/bundesliga/clubs',
  'serie-a':'https://www.legaseriea.it/en/team',
  'ligue-1':'https://ligue1.com/clubs'
};

const leagues = [
  {
    id: 'premier-league', name: 'Premier League', file: 'premier-league-2026-27-squads.json', teams: [
      'Arsenal', 'AFC Bournemouth', 'Aston Villa', 'Brentford', 'Brighton & Hove Albion', 'Chelsea',
      'Coventry City', 'Crystal Palace', 'Everton', 'Fulham', 'Hull City', 'Ipswich Town', 'Leeds United',
      'Liverpool', 'Manchester City', 'Manchester United', 'Newcastle United', 'Nottingham Forest',
      'Sunderland', 'Tottenham Hotspur'
    ]
  },
  {
    id: 'la-liga', name: 'La Liga', file: 'la-liga-2026-27-squads.json', teams: [
      'Athletic Club', 'Atletico Madrid', 'Osasuna', 'Celta Vigo', 'Deportivo Alaves', 'Elche', 'Barcelona',
      'Getafe', 'Levante', 'Malaga', 'Racing Santander', 'Rayo Vallecano', 'Deportivo La Coruna', 'Espanyol',
      'Real Betis', 'Real Madrid', 'Real Sociedad', 'Sevilla', 'Valencia', 'Villarreal'
    ]
  },
  {
    id: 'bundesliga', name: 'Bundesliga', file: 'bundesliga-2026-27-squads.json', teams: [
      'Augsburg', 'Union Berlin', 'Werder Bremen', 'Borussia Dortmund', 'Elversberg', 'Eintracht Frankfurt',
      'Freiburg', 'Hamburg', 'Hoffenheim', 'FC Koln', 'RB Leipzig', 'Bayer Leverkusen', 'Mainz 05',
      'Borussia Monchengladbach', 'Bayern Munich', 'Paderborn', 'Schalke 04', 'Stuttgart'
    ]
  },
  {
    id: 'serie-a', name: 'Serie A', file: 'serie-a-2026-27-squads.json', teams: [
      'Fiorentina', 'Frosinone', 'AC Milan', 'Monza', 'Parma', 'Sassuolo', 'Torino', 'Udinese', 'Venezia',
      'Atalanta', 'Bologna', 'Cagliari', 'Como', 'Genoa', 'Inter Milan', 'Juventus', 'Napoli', 'Lazio',
      'Roma', 'Lecce'
    ]
  },
  {
    id: 'ligue-1', name: 'Ligue 1', file: 'ligue-1-2026-27-squads.json', teams: [
      'Angers', 'Auxerre', 'Brest', 'Le Havre', 'Lens', 'Lille', 'Lorient', 'Lyon', 'Le Mans', 'Marseille',
      'Monaco', 'Nice', 'Paris FC', 'Paris Saint-Germain', 'Rennes', 'Strasbourg', 'Toulouse', 'Troyes'
    ]
  },
  {
    id: 'champions-league', name: 'Champions League Squad Source', file: 'champions-league-2026-27-squads.json', teams: [
      'AEK Athens', 'Bodo/Glimt', 'Club Brugge', 'Fenerbahce', 'Feyenoord', 'Galatasaray', 'LASK', 'Porto',
      'PSV', 'Sabah', 'Shakhtar Donetsk', 'Slavia Prague', 'Sporting CP', 'Viking', 'Slovan Bratislava'
    ]
  }
];

const eaAliases = new Map(Object.entries({
  'AFC Bournemouth': 'Bournemouth', 'Brighton & Hove Albion': 'Brighton', 'Ipswich Town': 'Ipswich',
  'Manchester United': 'Man Utd', 'Newcastle United': 'Newcastle Utd', 'Nottingham Forest': "Nott'm Forest",
  'Tottenham Hotspur': 'Spurs', 'Athletic Club': 'Athletic Bilbao', 'Atletico Madrid': 'Atlético de Madrid',
  'Osasuna': 'CA Osasuna', 'Celta Vigo': 'Celta', 'Deportivo Alaves': 'Deportivo Alavés', 'Elche': 'Elche CF',
  'Barcelona': 'FC Barcelona', 'Getafe': 'Getafe CF', 'Levante': 'Levante UD', 'Malaga': 'Málaga CF',
  'Racing Santander': 'R. Racing Club', 'Deportivo La Coruna': 'RC Deportivo', 'Espanyol': 'RCD Espanyol',
  'Sevilla': 'Sevilla FC', 'Valencia': 'Valencia CF', 'Villarreal': 'Villarreal CF',
  'Augsburg': 'FC Augsburg', 'Union Berlin': '1. FC Union Berlin', 'Werder Bremen': 'SV Werder Bremen',
  'Elversberg': 'SV Elversberg', 'Freiburg': 'SC Freiburg', 'Hamburg': 'Hamburger SV',
  'Hoffenheim': 'TSG Hoffenheim', 'FC Koln': '1. FC Köln', 'Bayer Leverkusen': 'Leverkusen',
  'Mainz 05': '1. FSV Mainz 05', 'Borussia Monchengladbach': "M'gladbach", 'Bayern Munich': 'FC Bayern München',
  'Paderborn': 'SC Paderborn 07', 'Schalke 04': 'FC Schalke 04', 'Stuttgart': 'VfB Stuttgart',
  'AC Milan': 'Milano FC', 'Inter Milan': 'Lombardia FC', 'Atalanta': 'Bergamo Calcio',
  'Napoli': 'SSC Napoli', 'Lazio': 'Latium', 'Lecce': 'US Lecce',
  'Angers': 'Angers SCO', 'Auxerre': 'AJ Auxerre', 'Brest': 'Stade Brestois 29', 'Le Havre': 'Havre AC',
  'Lens': 'RC Lens', 'Lille': 'LOSC Lille', 'Lorient': 'FC Lorient', 'Lyon': 'OL', 'Le Mans': 'Le Mans FC',
  'Marseille': 'OM', 'Monaco': 'AS Monaco', 'Nice': 'OGC Nice', 'Paris Saint-Germain': 'Paris SG',
  'Rennes': 'Stade Rennais FC', 'Strasbourg': 'RC Strasbourg Alsace', 'Toulouse': 'Toulouse FC',
  'Troyes': 'ESTAC Troyes', 'Bodo/Glimt': 'FK Bodø/Glimt', 'Porto': 'FC Porto',
  'Fenerbahce': 'Fenerbahçe', 'Viking': 'Viking FK', 'Slavia Prague': 'Slavia Praha'
}));

// EA's ratings landing page does not expose every lower-division/promoted club in
// its filter payload. These stable team IDs keep those official roster pages
// addressable and also disambiguate licensed and unlicensed display names.
const eaTeamOverrides = new Map(Object.entries({
  'AFC Bournemouth':[1943,'AFC Bournemouth'], 'Coventry City':[1800,'Coventry City'],
  'Hull City':[1952,'Hull City'], 'Athletic Club':[448,'Athletic Club'],
  'Union Berlin':[1831,'Union Berlin'], 'Elversberg':[580,'SV Elversberg'],
  'Eintracht Frankfurt':[1824,'Frankfurt'], 'Paderborn':[10030,'SC Paderborn 07'],
  'Schalke 04':[34,'FC Schalke 04'], 'Frosinone':[111657,'Frosinone'],
  'AC Milan':[131681,'Milano FC'], 'Monza':[111811,'Monza'],
  'Inter Milan':[131682,'Lombardia FC'], 'Lecce':[347,'Lecce'],
  'Strasbourg':[76,'Strasbourg'], 'Troyes':[294,'ESTAC Troyes'],
  'Feyenoord':[246,'Feyenoord'], 'PSV':[247,'PSV']
}));

const venues = new Map(Object.entries({
  'Coventry City': 'Coventry Building Society Arena', 'Hull City': 'MKM Stadium', 'Ipswich Town': 'Portman Road',
  'Malaga': 'La Rosaleda', 'Racing Santander': 'El Sardinero', 'Deportivo La Coruna': 'Estadio Riazor',
  'Elversberg': 'URSAPHARM-Arena', 'Paderborn': 'Home Deluxe Arena', 'Schalke 04': 'VELTINS-Arena',
  'Frosinone': 'Stadio Benito Stirpe', 'Monza': 'U-Power Stadium', 'Venezia': 'Stadio Pier Luigi Penzo',
  'Le Mans': 'Stade Marie-Marvingt', 'Troyes': "Stade de l'Aube", 'AEK Athens': 'OPAP Arena',
  'Bodo/Glimt': 'Aspmyra Stadion', 'Fenerbahce': 'Sukru Saracoglu Stadium', 'LASK': 'Raiffeisen Arena',
  'Sabah': 'Bank Respublika Arena', 'Slavia Prague': 'Fortuna Arena', 'Viking': 'SR-Bank Arena'
}));

const fallbackSquads = {
  Sabah: [
    [1,'Amin Ramazanov','GK',23,'Azerbaijan'],[12,'Rauf Ayyubov','GK',17,'Azerbaijan'],[92,'Stas Pokatilov','GK',33,'Kazakhstan'],[94,'Ravan Mirzammadov','GK',21,'Azerbaijan'],
    [3,'Steve Solvet','CB',30,'France'],[4,'Aden McCarthy','CB',22,'South Africa'],[5,'Rahman Dashdamirov','CB',26,'Azerbaijan'],[17,'Tellur Mutallimov','RB',31,'Azerbaijan'],[27,'Tymoteusz Puchacz','LB',27,'Poland'],[33,'Erivaldo Almeida','CB',26,'Brazil'],[80,'Akim Zedadka','RB',31,'Algeria'],
    [6,'Abdulakh Khaibulaev','CDM',25,'Azerbaijan'],[7,'Umarali Rakhmonaliev','CM',23,'Uzbekistan'],[10,'Aleksey Isaev','CAM',30,'Azerbaijan'],[11,'Kaheem Parris','RM',26,'Jamaica'],[13,'Ivan Lepinjica','CDM',27,'Croatia'],[16,'Rauf Rustamli','CM',23,'Azerbaijan'],[88,'Rodrigo Fernandes','CM',25,'Portugal'],[89,'Jafar Mukhtarov','CM',21,'Azerbaijan'],[95,'Shahin Ibrahimov','CAM',19,'Azerbaijan'],
    [8,'Christian Nwachukwu','LW',20,'Nigeria'],[20,'Joy-Lance Mickels','ST',32,'Rwanda'],[21,'Veljko Simic','RW',31,'Serbia'],[23,'Younes Lachaab','ST',21,'France'],[34,'Xander Severina','LW',25,'Netherlands'],[99,'Orphe Mbina','ST',25,'Gabon']
  ],
  'Slovan Bratislava': [
    [1,'Aleksandar Popovic','GK',26,'Serbia'],[32,'David Balog','GK',19,'Slovakia'],[44,'Matus Macik','GK',33,'Slovakia'],[71,'Dominik Takac','GK',27,'Slovakia'],
    [2,'Samuel Kozlovsky','LB',26,'Slovakia'],[6,'Kevin Wimmer','CB',33,'Austria'],[12,'Kenan Bajric','CB',31,'Slovenia'],[15,'Svetozar Markovic','CB',26,'Serbia'],[24,'Matus Tomasko','CB',16,'Slovakia'],[26,'Robert Tomanek','CB',20,'Slovakia'],[28,'Cesar Blackman','RB',28,'Panama'],[57,'Sandro Cruz','LB',25,'Angola'],
    [3,'Peter Pokorny','CDM',25,'Slovakia'],[5,'Rahim Ibrahim','CM',25,'Ghana'],[8,'Artur Gajdos','CAM',22,'Slovakia'],[11,'Tigran Barseghyan','RW',32,'Armenia'],[20,'Alen Mustafic','CM',27,'Bosnia and Herzegovina'],[25,'Leo Hofstadter','CM',16,'Slovakia'],[70,'Cristian Martinez','CM',29,'Panama'],[77,'Danylo Ihnatenko','CDM',29,'Ukraine'],[88,'Daiki Matsuoka','CM',25,'Japan'],
    [10,'Nino Marcelli','LW',21,'Slovakia'],[13,'Roman Cerepkai','ST',24,'Slovakia'],[14,'Alasana Yirajang','ST',21,'Gambia'],[19,'Manasse Kianga','ST',24,'South Africa'],[21,'Suleiman Camara','RW',24,'Gambia'],[29,'Alexej Maros','ST',21,'Slovakia'],[99,'Andraz Sporar','ST',32,'Slovenia']
  ]
};

// Official squad-page additions not present in EA's launch-card roster. Rows are
// [shirt number (0 = assign), name, position, age, nationality].
const officialSupplements = {
  'Racing Santander': [
    [13,'Julen Agirrezabala','GK',25,'Spain'],[31,'Alejandro Jimenez','GK',21,'Spain'],
    [5,'Mickael Carrascal','CB',21,'Colombia'],[22,'Pedro Felipe','CB',22,'Brazil'],
    [30,'Carlos Sanchez','CB',19,'Spain'],[0,'Jeanuel Belocian','CB',21,'France'],
    [0,'Aaron Martin','LB',29,'Spain'],[18,'Matteo Prati','CM',22,'Italy'],
    [26,'Marco Solorzano','CM',21,'Spain'],[33,'Jorge Castellanos','CM',20,'Spain'],
    [44,'Diego Fuentes','CM',19,'Spain'],[17,'Jaime Mata','ST',37,'Spain'],[19,'Ivan Luque','ST',21,'Spain']
  ],
  Brest: [
    [1,'Mathieu Patouillet','GK',22,'France'],[16,'Egil Selvik','GK',29,'Norway'],[49,'Romain Cagnon','GK',29,'France'],
    [0,'Ewen Mailly','CB',18,'France'],[4,'Gautier Lloris','CB',31,'France'],
    [6,'Mahamadou Diambou','CM',23,'Mali'],[7,'Joseph Nonge','CM',21,'Belgium'],[32,'Mael Laine','CM',19,'France']
  ],
  Strasbourg: [[1,'Filip Jorgensen','GK',24,'Denmark'],[60,'Gabin Kerckaert','GK',18,'France']]
};

function normalize(value) {
  return String(value ?? '').normalize('NFD').replace(/\p{Diacritic}/gu, '').replace(/&amp;/g, 'and')
    .replace(/[^a-z0-9]+/gi, ' ').trim().toLowerCase();
}

function slugify(value) { return normalize(value).replace(/\s+/g, '-'); }
function stripTags(value) { return value.replace(/<[^>]+>/g, '').replace(/&amp;/g, '&').replace(/&#x27;/g, "'").replace(/&quot;/g, '"').trim(); }
function decodeHtml(value) { return stripTags(value).replace(/\u([0-9a-f]{4})/gi, (_, hex) => String.fromCharCode(parseInt(hex, 16))); }
function readLastNumber(markup) { const values = stripTags(markup).match(/\d+/g); return values ? Number(values.at(-1)) : null; }

function loadExistingData() {
  const teams = new Map();
  const players = [];
  for (const file of fs.readdirSync(dataDir).filter(name => name.endsWith('2025-26-squads.json'))) {
    const data = JSON.parse(fs.readFileSync(path.join(dataDir, file), 'utf8'));
    for (const team of data.teams ?? []) {
      teams.set(normalize(team.name), team);
      for (const player of [...(team.startingXI ?? []), ...(team.substitutes ?? []), ...(team.reserves ?? [])]) players.push(player);
    }
  }
  return { teams, players };
}

function buildCountryProfiles(existingPlayers) {
  const profiles = new Map();
  for (const player of existingPlayers) {
    if (player.nationalityName && player.nationalityCode) profiles.set(normalize(player.nationalityName), {
      code: player.nationalityCode, name: player.nationalityName,
      flag: player.flagImagePath || `/Assets/Flags/${slugify(player.nationalityName)}.png`
    });
  }
  const manual = {
    Azerbaijan:'AZ', Kazakhstan:'KZ', Uzbekistan:'UZ', Rwanda:'RW', Slovakia:'SK', Slovenia:'SI',
    Panama:'PA', Angola:'AO', Armenia:'AM', Gambia:'GM', Gabon:'GA', Jamaica:'JM', 'South Africa':'ZA'
  };
  for (const [name, code] of Object.entries(manual)) profiles.set(normalize(name), { code, name, flag:`/Assets/Flags/${slugify(name)}.png` });
  return profiles;
}

async function fetchText(url) {
  let response = await fetch(url, { headers: { 'user-agent': 'Mozilla/5.0' } });
  if (!response.ok && url.includes('/games/ea-sports-fc/ratings/teams-ratings/')) {
    const localizedUrl = url.replace('ea.com/games/', 'ea.com/ro/games/');
    response = await fetch(localizedUrl, { headers: { 'user-agent': 'Mozilla/5.0' } });
  }
  if (!response.ok) throw new Error(`${response.status} ${url}`);
  return response.text();
}

async function buildEaDirectory() {
  const html = await fetchText(`${baseUrl}/games/ea-sports-fc/ratings`);
  const directory = new Map();
  for (const match of html.matchAll(/\{"id":(\d+),"label":"((?:[^"\\]|\\.)*)","imageUrl":"[^"]*\/l\d+\.png"/g)) {
    const id = Number(match[1]);
    const label = JSON.parse(`"${match[2]}"`);
    const key = normalize(label);
    const current = directory.get(key);
    if (id < 130000 && (!current || id < current.id)) directory.set(key, { id, label });
  }
  return directory;
}

function parseEaPlayers(html) {
  const players = [];
  for (const part of html.split('<tr class="Table_row__').slice(2)) {
    const row = part.split('</tr>')[0];
    const cells = [...row.matchAll(/<td\b[^>]*>[\s\S]*?<\/td>/g)].map(match => match[0]);
    if (cells.length < 12) continue;
    const profile = cells[1].match(/player-ratings\/[^"/]+\/(\d+)/);
    const nameMatch = cells[1].match(/Table_profileLabel__[^>]*>([^<]+)/);
    const numberMatch = cells[1].match(/Table_profileSupLabel__[^>]*>#(\d+)/);
    const nationalityMatch = cells[2].match(/<img alt="([^"]+)"/);
    const values = cells.slice(5, 12).map(readLastNumber);
    if (!profile || !nameMatch || !numberMatch || !nationalityMatch || values.some(value => value == null)) continue;
    const id = Number(profile[1]);
    const embedded = html.match(new RegExp(`\\{"id":${id},"rank":\\d+,"overallRating":\\d+,[\\s\\S]{0,700}?"birthdate":"([^"]*)"[\\s\\S]{0,500}?"preferredFoot":(\\d+)`));
    players.push({
      externalId: id, name: decodeHtml(nameMatch[1]), squadNumber: Number(numberMatch[1]),
      nationality: decodeHtml(nationalityMatch[1]), position: stripTags(cells[4]),
      overallRating: values[0], pace: values[1], shooting: values[2], passing: values[3],
      dribbling: values[4], defending: values[5], physical: values[6],
      birthdate: embedded?.[1] ?? '', preferredFoot: embedded?.[2] === '1' ? 'Left' : 'Right'
    });
  }
  return players;
}

function ageOnCutoff(birthdate) {
  if (!birthdate) return null;
  const [month, day, year] = birthdate.split(' ')[0].split('/').map(Number);
  let age = 2026 - year;
  if (month > 9 || (month === 9 && day > 2)) age--;
  return age;
}

function positionGroup(position) {
  if (position === 'GK') return 'GK';
  if (['CB','LB','RB','LWB','RWB'].includes(position)) return 'D';
  if (['CDM','CM','CAM','LM','RM'].includes(position)) return 'M';
  return 'F';
}

function potential(overall, age) {
  const headroom = age <= 19 ? 8 : age <= 21 ? 6 : age <= 23 ? 4 : age <= 25 ? 2 : 0;
  return Math.min(94, overall + headroom);
}

function findExistingPlayer(player, existingPlayers) {
  const key = normalize(player.name);
  const matches = existingPlayers.filter(candidate => normalize(candidate.name) === key);
  if (matches.length === 1) return matches[0];
  return matches.find(candidate => normalize(candidate.nationalityName) === normalize(player.nationality)) ?? null;
}

function makePlayer(source, existingPlayers, countryProfiles) {
  const old = findExistingPlayer(source, existingPlayers);
  const age = ageOnCutoff(source.birthdate) ?? old?.age ?? 24;
  const nationality = countryProfiles.get(normalize(source.nationality)) ?? {
    code: source.nationality.slice(0, 2).toUpperCase(), name: source.nationality,
    flag: `/Assets/Flags/${slugify(source.nationality)}.png`
  };
  return {
    playerId: `ea:${source.externalId}`,
    name: source.name, squadNumber: source.squadNumber, position: source.position,
    preferredPosition: source.position, secondaryPositions: old?.secondaryPositions ?? [],
    preferredFoot: source.preferredFoot, nationalityCode: nationality.code, nationalityName: nationality.name,
    flagImagePath: nationality.flag, overallRating: source.overallRating, age,
    potentialOverall: potential(source.overallRating, age), pace: source.pace, shooting: source.shooting,
    passing: source.passing, dribbling: source.dribbling, defending: source.defending, physical: source.physical,
    stamina: old?.stamina ?? Math.max(60, Math.min(95, Math.round((source.physical + source.overallRating) / 2))),
    traits: old?.traits ?? [], morale: 50,
    ...(old?.contractEndYear ? { contractEndYear: old.contractEndYear } : {}),
    ...(old?.weeklyWage ? { weeklyWage: old.weeklyWage } : {}),
    ...(old?.releaseClause ? { releaseClause: old.releaseClause } : {})
  };
}

function makeFallbackPlayers(teamName, rows, countryProfiles) {
  return rows.map(([number,name,position,age,nationality], index) => {
    const profile = countryProfiles.get(normalize(nationality)) ?? { code:nationality.slice(0,2).toUpperCase(), name:nationality, flag:`/Assets/Flags/${slugify(nationality)}.png` };
    const base = teamName === 'Sabah' ? 67 : 70;
    const overall = Math.max(60, base + ((number * 7 + index * 3) % 7) - 3);
    const group = positionGroup(position);
    const pace = Math.min(88, overall + (group === 'F' ? 5 : group === 'D' ? 1 : 2));
    const shooting = Math.max(35, overall + (group === 'F' ? 2 : group === 'M' ? -5 : -25));
    const passing = Math.max(45, overall + (group === 'M' ? 2 : group === 'F' ? -5 : -8));
    const dribbling = Math.max(42, overall + (group === 'F' ? 1 : group === 'M' ? 0 : -10));
    const defending = Math.max(30, overall + (group === 'D' ? 2 : group === 'M' ? -5 : -25));
    return { playerId:`uefa:${slugify(teamName)}:${slugify(name)}`, name, squadNumber:number, position,
      preferredPosition:position, secondaryPositions:[], preferredFoot:'Right', nationalityCode:profile.code,
      nationalityName:profile.name, flagImagePath:profile.flag, overallRating:overall, age,
      potentialOverall:potential(overall, age), pace, shooting, passing, dribbling, defending,
      physical:Math.max(50, overall), stamina:Math.max(65, overall + 5), traits:[], morale:50 };
  });
}

function makeOfficialSupplement(row, countryProfiles, eaPlayerPool, existingPlayers) {
  const [squadNumber,name,position,age,nationality] = row;
  const eaMatch = eaPlayerPool.find(player => normalize(player.name) === normalize(name));
  if (eaMatch) return { ...makePlayer(eaMatch, existingPlayers, countryProfiles), squadNumber:squadNumber || eaMatch.squadNumber };
  const profile = countryProfiles.get(normalize(nationality)) ?? { code:nationality.slice(0,2).toUpperCase(), name:nationality, flag:`/Assets/Flags/${slugify(nationality)}.png` };
  const overall = 68;
  const group = positionGroup(position);
  return { playerId:`official:${slugify(name)}:${age}:${profile.code.toLowerCase()}`, name, squadNumber, position,
    preferredPosition:position, secondaryPositions:[], preferredFoot:'Right', nationalityCode:profile.code,
    nationalityName:profile.name, flagImagePath:profile.flag, overallRating:overall, age,
    potentialOverall:potential(overall, age), pace:group === 'F' ? 73 : 67, shooting:group === 'F' ? 67 : 48,
    passing:group === 'M' ? 68 : 58, dribbling:group === 'F' ? 69 : 61,
    defending:group === 'D' ? 69 : 45, physical:68, stamina:72, traits:[], morale:50 };
}

function takeBest(remaining, group, count) {
  const selected = remaining.filter(player => positionGroup(player.position) === group)
    .sort((a,b) => b.overallRating - a.overallRating).slice(0, count);
  for (const player of selected) remaining.splice(remaining.indexOf(player), 1);
  return selected;
}

function partitionSquad(players) {
  const remaining = [...players];
  const startingXI = [
    ...takeBest(remaining,'GK',1), ...takeBest(remaining,'D',4),
    ...takeBest(remaining,'M',3), ...takeBest(remaining,'F',3)
  ];
  while (startingXI.length < 11 && remaining.length) startingXI.push(remaining.sort((a,b) => b.overallRating-a.overallRating).shift());
  const substitutes = [
    ...takeBest(remaining,'GK',1), ...takeBest(remaining,'D',3),
    ...takeBest(remaining,'M',3), ...takeBest(remaining,'F',2)
  ];
  while (substitutes.length < 12 && remaining.length) substitutes.push(remaining.sort((a,b) => b.overallRating-a.overallRating).shift());
  return { startingXI, substitutes, reserves:remaining.sort((a,b) => b.overallRating-a.overallRating) };
}

function ensureUniqueNumbers(players) {
  const used = new Set();
  for (const player of players) {
    if (player.squadNumber >= 1 && player.squadNumber <= 99 && !used.has(player.squadNumber)) { used.add(player.squadNumber); continue; }
    player.squadNumber = Array.from({length:99},(_,i)=>i+1).find(number => !used.has(number));
    used.add(player.squadNumber);
  }
}

function validate(files) {
  const ownership = new Map();
  for (const file of files) for (const team of file.data.teams) {
    const players = [...team.startingXI, ...team.substitutes, ...team.reserves];
    if (team.startingXI.length !== 11 || team.substitutes.length !== 12 || !team.startingXI.some(p=>p.position==='GK') || !team.substitutes.some(p=>p.position==='GK')) throw new Error(`${team.name} has an invalid matchday squad (${players.length} players; positions: ${players.map(p=>p.position).join(', ')}).`);
    if (players.length < 23) throw new Error(`${team.name} has only ${players.length} senior players.`);
    if (new Set(players.map(p=>p.playerId)).size !== players.length) throw new Error(`${team.name} has duplicate player IDs.`);
    if (new Set(players.map(p=>p.squadNumber)).size !== players.length) throw new Error(`${team.name} has duplicate shirt numbers.`);
    for (const player of players) {
      const owner = ownership.get(player.playerId);
      if (owner && owner !== team.name) throw new Error(`${player.name} (${player.playerId}) belongs to both ${owner} and ${team.name}.`);
      ownership.set(player.playerId, team.name);
    }
  }
  return ownership.size;
}

const existing = loadExistingData();
const countryProfiles = buildCountryProfiles(existing.players);
const directory = await buildEaDirectory();
const targetTeams = [...new Set(leagues.flatMap(league => league.teams))];
const fetched = new Map();
const unmatchedTeams = [];

await Promise.all(targetTeams.map(async teamName => {
  if (fallbackSquads[teamName]) return;
  const override = eaTeamOverrides.get(teamName);
  const eaName = eaAliases.get(teamName) ?? teamName;
  const entry = override ? { id:override[0], label:override[1] } : directory.get(normalize(eaName));
  if (!entry) { unmatchedTeams.push({teamName, eaName}); return; }
  const url = `${baseUrl}/games/ea-sports-fc/ratings/teams-ratings/${slugify(entry.label)}/${entry.id}`;
  const players = parseEaPlayers(await fetchText(url));
  fetched.set(teamName, { players, url });
}));

if (unmatchedTeams.length) throw new Error(`FC 27 teams not resolved: ${JSON.stringify(unmatchedTeams)}`);

const eaPlayerPool = [...fetched.values()].flatMap(team => team.players);

const outputs = leagues.map(league => {
  const teams = league.teams.map(teamName => {
    const oldTeam = existing.teams.get(normalize(teamName));
    const players = fallbackSquads[teamName]
      ? makeFallbackPlayers(teamName, fallbackSquads[teamName], countryProfiles)
      : fetched.get(teamName).players.map(player => makePlayer(player, existing.players, countryProfiles));
    for (const row of officialSupplements[teamName] ?? []) {
      if (!players.some(player => normalize(player.name) === normalize(row[1]))) {
        players.push(makeOfficialSupplement(row, countryProfiles, eaPlayerPool, existing.players));
      }
    }
    ensureUniqueNumbers(players);
    const squad = partitionSquad(players);
    return {
      teamId: slugify(teamName), name: teamName, shortName: oldTeam?.shortName ?? teamName,
      city: oldTeam?.city ?? '', logoKey: slugify(teamName),
      venue: venues.get(teamName) ?? oldTeam?.venue ?? `${teamName} Stadium`,
      stadiumName: venues.get(teamName) ?? oldTeam?.stadiumName ?? `${teamName} Stadium`,
      formation: oldTeam?.formation ?? '4-3-3', ...squad
    };
  });
  const sources = league.id === 'champions-league'
    ? ['https://www.uefa.com/uefachampionsleague/clubs/','https://www.ea.com/games/ea-sports-fc/ratings']
    : [officialLeagueSources[league.id],'https://www.ea.com/games/ea-sports-fc/ratings'];
  return { file:league.file, data:{ leagueId:league.id, leagueName:league.name, season, sourceLastChecked:cutoff, sources, teams } };
});

const transferredPlayerTargets = new Map(Object.entries(officialSupplements).flatMap(([teamName, rows]) => rows.map(row => [normalize(row[1]), teamName])));
for (const output of outputs) for (const team of output.data.teams) {
  const retained = [];
  for (const group of ['startingXI','substitutes','reserves']) {
    retained.push(...team[group].filter(player => {
      const target = transferredPlayerTargets.get(normalize(player.name));
      return !target || target === team.name;
    }));
  }
  Object.assign(team, partitionSquad(retained));
}

const incomplete = outputs.flatMap(output => output.data.teams.map(team => { const roster=[...team.startingXI,...team.substitutes,...team.reserves]; return { team:team.name, count:roster.length, goalkeepers:roster.filter(player => player.position === 'GK').length, players:roster.map(player => player.name) }; })).filter(team => team.count < 23 || team.goalkeepers < 2);
if (incomplete.length) console.error(`Incomplete EA rosters: ${JSON.stringify(incomplete)}`);
const uniquePlayers = validate(outputs);
if (writeChanges) for (const output of outputs) fs.writeFileSync(path.join(dataDir, output.file), `${JSON.stringify(output.data,null,2)}\n`);

const fallbacks = [...Object.entries(fallbackSquads).flatMap(([team,rows]) => rows.map(row => ({team,player:row[1],reason:'EA FC 27 team unavailable; UEFA squad and conservative club median used'}))),
  ...Object.entries(officialSupplements).flatMap(([team,rows]) => rows.filter(row => !eaPlayerPool.some(player => normalize(player.name) === normalize(row[1]))).map(row => ({team,player:row[1],reason:'Not present in EA FC 27 launch roster; official squad identity and conservative median used'})))];
console.log(JSON.stringify({ mode:writeChanges?'write':'dry-run', season, cutoff, files:outputs.map(output=>({file:output.file,teams:output.data.teams.length,players:output.data.teams.reduce((sum,team)=>sum+team.startingXI.length+team.substitutes.length+team.reserves.length,0)})), uniquePlayers, fallbackCount:fallbacks.length, fallbacks }, null, 2));
