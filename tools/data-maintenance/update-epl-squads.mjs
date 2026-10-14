import fs from 'node:fs';
import path from 'node:path';

const writeChanges = process.argv.includes('--write');
const baseUrl = 'https://www.ea.com';
const dataDir = 'src/FootballSimulation.Core/Data/Json';
const squadsPath = path.join(dataDir, 'premier-league-2025-26-squads.json');
const playersPath = path.join(dataDir, 'premier-league-2025-26-players.json');
const officialSquadsUrl = 'https://www.premierleague.com/en/news/4580687/202526-premier-league-squad-lists';
const maxPlayablePlayers = 23;
const minPlayablePlayers = 18;

const eplTeamUrls = [
  '/en/games/ea-sports-fc/ratings/teams-ratings/arsenal/1',
  '/en/games/ea-sports-fc/ratings/teams-ratings/afc-bournemouth/1943',
  '/en/games/ea-sports-fc/ratings/teams-ratings/aston-villa/2',
  '/en/games/ea-sports-fc/ratings/teams-ratings/brentford/1925',
  '/en/games/ea-sports-fc/ratings/teams-ratings/brighton/1808',
  '/en/games/ea-sports-fc/ratings/teams-ratings/burnley/1796',
  '/en/games/ea-sports-fc/ratings/teams-ratings/chelsea/5',
  '/en/games/ea-sports-fc/ratings/teams-ratings/crystal-palace/1799',
  '/en/games/ea-sports-fc/ratings/teams-ratings/everton/7',
  '/en/games/ea-sports-fc/ratings/teams-ratings/fulham/144',
  '/en/games/ea-sports-fc/ratings/teams-ratings/leeds-united/8',
  '/en/games/ea-sports-fc/ratings/teams-ratings/liverpool/9',
  '/en/games/ea-sports-fc/ratings/teams-ratings/manchester-city/10',
  '/en/games/ea-sports-fc/ratings/teams-ratings/man-utd/11',
  '/en/games/ea-sports-fc/ratings/teams-ratings/newcastle-utd/13',
  '/en/games/ea-sports-fc/ratings/teams-ratings/nott-m-forest/14',
  '/en/games/ea-sports-fc/ratings/teams-ratings/sunderland/106',
  '/en/games/ea-sports-fc/ratings/teams-ratings/spurs/18',
  '/en/games/ea-sports-fc/ratings/teams-ratings/west-ham/19',
  '/en/games/ea-sports-fc/ratings/teams-ratings/wolves/110'
];

const eaTeamAliases = new Map(Object.entries({
  'afc bournemouth': 'AFC Bournemouth',
  'brighton': 'Brighton & Hove Albion',
  'man utd': 'Manchester United',
  'newcastle utd': 'Newcastle United',
  'nott m forest': 'Nottingham Forest',
  'spurs': 'Tottenham Hotspur',
  'west ham': 'West Ham United',
  'wolves': 'Wolverhampton Wanderers'
}));

const registeredNameAliases = createNormalizedMap({
  'dennis william jonathon': 'Will Dennis',
  'forster fraser gerard': 'Fraser Forster',
  'sadi dominic wadi': 'Dominic Sadi',
  'arrizabalaga revuelta kepa': 'Kepa Arrizabalaga',
  'dos santos magalhaes gabriel': 'Gabriel',
  'fernando de jesus gabriel': 'Gabriel Jesus',
  'teodoro martinelli silva gabriel': 'Gabriel Martinelli',
  'zubimendi ibanez martin': 'Martin Zubimendi',
  'bakumo abraham kevin oghenetega tamaraebi': 'Tammy Abraham',
  'martinez romero damian emiliano': 'Emiliano Martinez',
  'nilsson lindelof victor jorgen': 'Victor Lindelof',
  'soares de paulo douglas luiz': 'Douglas Luiz',
  'mvom onana amadou ba z': 'Amadou Onana',
  'freitas gouveia de carvalho fabio leandro': 'Fabio Carvalho',
  'nascimento rodrigues igor thiago': 'Igor Thiago',
  'valdimarsson hakon rafn': 'Hakon Valdimarsson',
  'adedokun valentino mayowa': 'Valentino Adedokun',
  'dos santos de paulo igor julio': 'Igor Julio',
  'gomez amarilla diego alexander': 'Diego Gomez',
  'van hecke jan paul': 'Jan Paul van Hecke',
  'mcgill thomas peter wayne': 'Tom McGill',
  'rushworth carl andrew': 'Carl Rushworth',
  'morris luis florentina ibrain': 'Florentino',
  'pires silva lucas': 'Lucas Pires',
  'tresor ndayishimiye mike': 'Mike Tresor',
  'mejbri hannibal': 'Hannibal',
  'adarabioyo abdul nasir oluwatosin': 'Tosin Adarabioyo',
  'badiashile mukinayi benoit ntambue': 'Benoit Badiashile',
  'caicedo corozo moises isaac': 'Moises Caicedo',
  'fernandez enzo jeremias': 'Enzo Fernandez',
  'junqueira de jesus joao pedro': 'Joao Pedro',
  'lomba neto pedro': 'Pedro Neto',
  'lynch sanchez robert': 'Robert Sanchez',
  'samuels colwill levi lamar': 'Levi Colwill',
  'almeida de oliveira goncalves estevao willian': 'Estevao',
  'sharman lowe teddy samuel': 'Teddy Sharman-Lowe',
  'garnacho ferreyra alejandro': 'Alejandro Garnacho',
  'nascimento dos santos andrey': 'Andrey Santos',
  'munoz mejia daniel': 'Daniel Munoz',
  'pino santos yeremy jesus': 'Yeremy Pino',
  'gomes betuncal norberto bercique': 'Beto',
  'welch reece belfield': 'Reece Welch',
  'borto alexander paul': 'Alex Borto',
  'jimenez rodriguez raul alonso': 'Raul Jimenez',
  'muniz carvalho rodrigo': 'Rodrigo Muniz',
  'santos lopes de macedo kevin': 'Kevin',
  'becker alisson ramses': 'Alisson',
  'konate ibrahima': 'Ibrahima Konate',
  'mac allister alexis': 'Alexis Mac Allister',
  'van dijk virgil': 'Virgil van Dijk',
  'dos santos gato alves dias ruben': 'Ruben Dias',
  'hernandez cascante rodrigo': 'Rodri',
  'gonzalez iglesias nicolas': 'Nico Gonzalez',
  'mota veiga de carvalho e silva bernardo': 'Bernardo Silva',
  'de oliveira nunes dos reis vitor': 'Vitor Reis',
  'moreira de oliveira savio': 'Savio',
  'borges fernandes bruno miguel': 'Bruno Fernandes',
  'casimiro carlos henrique': 'Casemiro',
  'dalot teixeira jose diogo': 'Diogo Dalot',
  'santos carneiro da cunha matheus': 'Matheus Cunha',
  'mee dermot william': 'Dermot Mee',
  'ugarte ribeiro manuel': 'Manuel Ugarte',
  'apolinario de lira joelinton cassio': 'Joelinton',
  'guimaraes rodriguez moura bruno': 'Bruno Guimaraes',
  'kraft emil henry kristoffer': 'Emil Krafth',
  'schar fabian lukas': 'Fabian Schar',
  'costa dos santos murillo santiago': 'Murillo',
  'maciel da cruz igor jesus': 'Igor Jesus',
  'rodrigues da silva felipe': 'Morato',
  'le fee enzo jeremy': 'Enzo Le Fee',
  'ramirez nilson david angulo': 'Nilson Angulo',
  'ellborg melker ake': 'Melker Ellborg',
  'richardson adam lee': 'Adam Richardson',
  'poveda ocampo ian carlo': 'Ian Poveda',
  'de Andrade richarlison': 'Richarlison',
  'lobo alves p costa palhinha goncalves joao maria': 'Joao Palhinha',
  'spence diop djed': 'Djed Spence',
  'van de ven micky': 'Micky van de Ven',
  'castellanos gimenez valentin mariano jose': 'Taty Castellanos',
  'disasi mhakinis belho axel': 'Axel Disasi',
  'fabianski lukasz marek': 'Lukasz Fabianski',
  'walker peters kyle leonardus': 'Kyle Walker-Peters',
  'wan bissaka aaron': 'Aaron Wan-Bissaka',
  'agbadou badobre emmanuel elysee djedje': 'Emmanuel Agbadou',
  'arias andrade jhon adolfo': 'Jhon Arias',
  'arokodare tolusewalase emmanuel': 'Tolu Arokodare',
  'arokodare tolustuwalase emmanuel': 'Tolu Arokodare',
  'arokodare toluwalase emmanuel': 'Tolu Arokodare',
  'bellegarde jeanricner': 'Jean-Ricner Bellegarde',
  'bueno lopez hugo': 'Hugo Bueno',
  'gomes tote antonio': 'Toti',
  'gomes da silva joao victor': 'Joao Gomes',
  'malheiro de sa jose pedro': 'Jose Sa',
  'trindade da costa neto andre': 'Andre',
  'griffiths harvey lawson': 'Harvey Griffiths',
  'lembikisa dexter joeng woo': 'Dexter Lembikisa'
});

const countryFallbacks = new Map(Object.entries({
  Argentina: ['AR', '/Assets/Flags/argentina.png'],
  Australia: ['AU', '/Assets/Flags/australia.png'],
  Austria: ['AT', '/Assets/Flags/austria.png'],
  Belgium: ['BE', '/Assets/Flags/belgium.png'],
  Bosnia: ['BA', '/Assets/Flags/bosnia-and-herzegovina.png'],
  Brazil: ['BR', '/Assets/Flags/brazil.png'],
  Cameroon: ['CM', '/Assets/Flags/cameroon.png'],
  Canada: ['CA', '/Assets/Flags/canada.png'],
  Colombia: ['CO', '/Assets/Flags/colombia.png'],
  Croatia: ['HR', '/Assets/Flags/croatia.png'],
  Czechia: ['CZ', '/Assets/Flags/czech-republic.png'],
  Denmark: ['DK', '/Assets/Flags/denmark.png'],
  Ecuador: ['EC', '/Assets/Flags/ecuador.png'],
  Egypt: ['EG', '/Assets/Flags/egypt.png'],
  England: ['GB-ENG', '/Assets/Flags/england.png'],
  France: ['FR', '/Assets/Flags/france.png'],
  Germany: ['DE', '/Assets/Flags/germany.png'],
  Ghana: ['GH', '/Assets/Flags/ghana.png'],
  Italy: ['IT', '/Assets/Flags/italy.png'],
  Japan: ['JP', '/Assets/Flags/japan.png'],
  Netherlands: ['NL', '/Assets/Flags/netherlands.png'],
  Nigeria: ['NG', '/Assets/Flags/nigeria.png'],
  Norway: ['NO', '/Assets/Flags/norway.png'],
  Poland: ['PL', '/Assets/Flags/poland.png'],
  Portugal: ['PT', '/Assets/Flags/portugal.png'],
  Scotland: ['GB-SCT', '/Assets/Flags/scotland.png'],
  Senegal: ['SN', '/Assets/Flags/senegal.png'],
  Serbia: ['RS', '/Assets/Flags/serbia.png'],
  Spain: ['ES', '/Assets/Flags/spain.png'],
  Sweden: ['SE', '/Assets/Flags/sweden.png'],
  Switzerland: ['CH', '/Assets/Flags/switzerland.png'],
  Turkey: ['TR', '/Assets/Flags/turkey.png'],
  Ukraine: ['UA', '/Assets/Flags/ukraine.png'],
  Uruguay: ['UY', '/Assets/Flags/uruguay.png'],
  Wales: ['GB-WLS', '/Assets/Flags/wales.png']
}));

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

function createNormalizedMap(values) {
  return new Map(Object.entries(values).map(([key, value]) => [normalizeKey(key), value]));
}

function decodeHtml(value) {
  return String(value ?? '')
    .replace(/&nbsp;/g, ' ')
    .replace(/&amp;/g, '&')
    .replace(/&#x27;/g, "'")
    .replace(/&quot;/g, '"')
    .replace(/&eacute;/g, 'é')
    .replace(/&Eacute;/g, 'É');
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
    .replace(/[^a-z0-9]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

function slugify(value) {
  return normalizeKey(value).replace(/\s+/g, '-');
}

function stripTags(value) {
  return decodeHtml(String(value ?? '').replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ').trim());
}

function tokenSet(value) {
  const ignored = new Set(['de', 'da', 'das', 'do', 'dos', 'del', 'di', 'den', 'van', 'der', 'jr', 'junior', 'ii']);
  return new Set(normalizeKey(value).split(' ').filter(token => token.length > 1 && !ignored.has(token)));
}

function tokenScore(left, right) {
  const leftTokens = tokenSet(left);
  const rightTokens = tokenSet(right);
  if (leftTokens.size === 0 || rightTokens.size === 0) {
    return 0;
  }

  let intersection = 0;
  for (const token of leftTokens) {
    if (rightTokens.has(token)) {
      intersection += 1;
    }
  }

  return (2 * intersection) / (leftTokens.size + rightTokens.size);
}

function nameScore(entry, candidateName) {
  const candidateKey = normalizeKey(candidateName);
  let best = 0;

  for (const name of entry.candidateNames) {
    const key = normalizeKey(name);
    if (key && key === candidateKey) {
      best = Math.max(best, 1.25);
    }

    best = Math.max(best, tokenScore(name, candidateName));
  }

  return best;
}

function createCandidateNames(registeredName) {
  const cleanName = decodeHtml(registeredName).replace(/\*/g, '').trim();
  const alias = registeredNameAliases.get(normalizeKey(cleanName));
  const names = new Set([cleanName]);

  if (alias) {
    names.add(alias);
  }

  const [surnamePart, givenPart = ''] = cleanName.split(',').map(part => part.trim());
  const surnameTokens = surnamePart.split(/\s+/).filter(Boolean);
  const givenTokens = givenPart.split(/\s+/).filter(Boolean);

  if (givenTokens.length > 0) {
    const firstGiven = givenTokens[0];
    const lastGiven = givenTokens.at(-1);
    const lastSurname = surnameTokens.at(-1);

    names.add(`${givenPart} ${surnamePart}`.trim());
    names.add(`${firstGiven} ${lastSurname}`.trim());
    names.add(`${lastGiven} ${lastSurname}`.trim());

    for (const surnameToken of surnameTokens) {
      names.add(`${firstGiven} ${surnameToken}`.trim());
      names.add(`${lastGiven} ${surnameToken}`.trim());
      names.add(surnameToken);
    }
  } else {
    names.add(surnamePart);
  }

  return [...names].filter(Boolean);
}

function readLastNumber(markup) {
  const numbers = stripTags(markup).match(/\d+/g);
  return numbers ? Number.parseInt(numbers.at(-1), 10) : null;
}

function readAttribute(markup, name) {
  const match = String(markup ?? '').match(new RegExp(`${name}="([^"]*)"`));
  return match ? decodeHtml(match[1]) : '';
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
    const nat = readAttribute(cells[2].match(/<img [^>]*>/)?.[0] ?? '', 'alt');
    const values = cells.slice(5, 12).map(readLastNumber);

    if (!player || !team || values.some(value => value == null)) {
      continue;
    }

    rows.push({
      player,
      team: eaTeamAliases.get(normalizeKey(team)) ?? team,
      nat,
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

async function fetchText(url) {
  const response = await fetch(url, { headers: { 'user-agent': 'Mozilla/5.0' } });
  return await response.text();
}

function parseOfficialSquads(html) {
  const result = new Map();
  const headings = [...html.matchAll(/<h5>[\s\S]*?<a [^>]*>([^<]+)<\/a><\/h5>/g)];

  for (let index = 0; index < headings.length; index += 1) {
    const teamName = decodeHtml(headings[index][1]).trim();
    const start = headings[index].index + headings[index][0].length;
    const end = headings[index + 1]?.index ?? html.indexOf('<h6', start);
    const block = html.slice(start, end > start ? end : undefined).replace(/<br\s*\/?>/g, '\n');
    const text = stripTags(block)
      .replace(/Premier League - Squad List 2025\/26/g, '')
      .replace(/\s+\n/g, '\n');
    const seniorStart = text.indexOf('25 Squad players');
    const under21Start = text.indexOf('U21 players');

    if (seniorStart < 0 || under21Start < 0) {
      continue;
    }

    const seniorText = text.slice(seniorStart, under21Start);
    const under21Text = text.slice(under21Start);
    result.set(teamName, {
      senior: parseOfficialEntries(seniorText),
      under21: parseOfficialEntries(under21Text)
    });
  }

  return result;
}

function parseOfficialEntries(text) {
  return [...text.matchAll(/\b\d+\s+([^0-9]+?)(?=\s+\d+\s+[A-ZÀ-ÖØ-Þ]|\s*$)/g)]
    .map(match => match[1].replace(/\*/g, '').trim())
    .filter(name => !/squad players|home grown|u21 players/i.test(name))
    .filter(Boolean)
    .map(name => ({
      registeredName: name,
      candidateNames: createCandidateNames(name)
    }));
}

function allTeamPlayers(team) {
  return [...team.startingXI, ...team.substitutes];
}

function roleOf(position) {
  const exact = normalizeKey(position).toUpperCase();
  if (exact === 'GK' || exact === 'GOALKEEPER') return 'GK';
  if (['CB', 'LB', 'RB', 'LWB', 'RWB'].includes(exact)) return 'DEF';
  if (['CDM', 'CM', 'CAM', 'LM', 'RM'].includes(exact)) return 'MID';
  return 'FWD';
}

function secondaryPositions(position) {
  const exact = String(position ?? '').toUpperCase();
  const map = {
    GK: [],
    CB: ['RB', 'LB', 'CDM'],
    LB: ['LWB', 'CB', 'LM'],
    RB: ['RWB', 'CB', 'RM'],
    LWB: ['LB', 'LM', 'LW'],
    RWB: ['RB', 'RM', 'RW'],
    CDM: ['CM', 'CB', 'CAM'],
    CM: ['CDM', 'CAM', 'LM', 'RM'],
    CAM: ['CM', 'CF', 'RW', 'LW'],
    LM: ['LW', 'CM', 'LB'],
    RM: ['RW', 'CM', 'RB'],
    LW: ['LM', 'RW', 'ST'],
    RW: ['RM', 'LW', 'ST'],
    CF: ['ST', 'CAM', 'LW', 'RW'],
    ST: ['CF', 'LW', 'RW']
  };

  return map[exact] ?? ['CM'];
}

function findBestEntryMatch(entry, candidates, minimumScore = 0.58) {
  const matches = candidates
    .map(candidate => ({ candidate, score: nameScore(entry, candidate.name ?? candidate.player) }))
    .filter(match => match.score >= minimumScore)
    .sort((left, right) => right.score - left.score);

  return matches[0] ?? null;
}

function buildCountryMap(squadsFile) {
  const countries = new Map();

  for (const team of squadsFile.teams) {
    for (const player of allTeamPlayers(team)) {
      if (player.nationalityName && player.nationalityCode && player.flagImagePath) {
        countries.set(player.nationalityName, [player.nationalityCode, player.flagImagePath]);
      }
    }
  }

  for (const [country, value] of countryFallbacks) {
    if (!countries.has(country)) {
      countries.set(country, value);
    }
  }

  return countries;
}

function createFallbackPlayer(entry, team, rating, countries, usedNumbers, sourcePlayer) {
  const displayName = rating?.player ?? entry.candidateNames.find(name => !name.includes(',')) ?? entry.registeredName;
  const position = rating?.position ?? sourcePlayer?.preferredPosition ?? sourcePlayer?.position ?? 'CM';
  const nat = rating?.nat ?? sourcePlayer?.nationalityName ?? 'England';
  const [nationalityCode, flagImagePath] = countries.get(nat) ?? countryFallbacks.get(nat) ?? ['GB-ENG', '/Assets/Flags/england.png'];
  const preferredSquadNumber = getPreferredSquadNumber(displayName);
  const squadNumber = preferredSquadNumber && !usedNumbers.has(preferredSquadNumber)
    ? preferredSquadNumber
    : sourcePlayer?.squadNumber && !usedNumbers.has(sourcePlayer.squadNumber)
    ? sourcePlayer.squadNumber
    : nextSquadNumber(usedNumbers, position);

  usedNumbers.add(squadNumber);

  return {
    playerId: null,
    name: displayName,
    squadNumber,
    position,
    preferredPosition: position,
    secondaryPositions: getKnownSecondaryPositions(displayName) ?? secondaryPositions(position),
    preferredFoot: null,
    nationality: null,
    nationalityCode,
    nationalityName: nat,
    flagEmoji: null,
    flagImagePath,
    disciplineRating: null,
    overallRating: rating?.overallRating ?? sourcePlayer?.overallRating ?? estimateOverall(position),
    age: getKnownAge(displayName) ?? sourcePlayer?.age ?? estimateAge(entry, rating),
    potentialOverall: getKnownPotential(displayName) ?? sourcePlayer?.potentialOverall ?? estimatePotential(rating?.overallRating ?? sourcePlayer?.overallRating ?? estimateOverall(position)),
    pace: rating?.pace ?? sourcePlayer?.pace ?? null,
    shooting: rating?.shooting ?? sourcePlayer?.shooting ?? null,
    passing: rating?.passing ?? sourcePlayer?.passing ?? null,
    dribbling: rating?.dribbling ?? sourcePlayer?.dribbling ?? null,
    defending: rating?.defending ?? sourcePlayer?.defending ?? null,
    physical: rating?.physical ?? sourcePlayer?.physical ?? null,
    transferStatus: null,
    role: null,
    rejectTransferOffers: null,
    contractEndYear: null,
    weeklyWage: null,
    releaseClause: null,
    contractStatus: null,
    stamina: defaultStamina(position),
    fatigue: 0,
    form: 'Average',
    formStatus: null,
    isStarter: false,
    traits: [],
    morale: 50,
    isInjured: false,
    injuryType: null,
    injurySeverity: null,
    injuryRecoveryMatches: null,
    isSeasonEndingInjury: null,
    isSuspended: null,
    suspendedMatches: null,
    matchesPlayedRecently: 0,
    recentMatchMinutes: [],
    consecutiveFullMatches: 0,
    seasonFatigue: 0,
    consecutiveStarts: 0
  };
}

function getPreferredSquadNumber(playerName) {
  return normalizeKey(playerName) === 'estevao' ? 41 : null;
}

function getKnownAge(playerName) {
  return normalizeKey(playerName) === 'estevao' ? 19 : null;
}

function getKnownPotential(playerName) {
  return normalizeKey(playerName) === 'estevao' ? 88 : null;
}

function getKnownSecondaryPositions(playerName) {
  return normalizeKey(playerName) === 'estevao' ? ['LW', 'CAM'] : null;
}

function nextSquadNumber(usedNumbers, position) {
  const preferred = roleOf(position) === 'GK'
    ? [1, 13, 22, 23, 31, 32, 40]
    : [2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 14, 15, 16, 17, 18, 19, 20, 21, 24, 25, 26, 27, 28, 29, 30];

  for (const number of preferred) {
    if (!usedNumbers.has(number)) {
      return number;
    }
  }

  for (let number = 1; number <= 99; number += 1) {
    if (!usedNumbers.has(number)) {
      return number;
    }
  }

  return 99;
}

function estimateOverall(position) {
  return roleOf(position) === 'GK' ? 70 : 72;
}

function estimatePotential(overall) {
  return Math.min(99, Math.max(overall, overall + 2));
}

function estimateAge(entry) {
  return entry.group === 'under21' ? 20 : 26;
}

function defaultStamina(position) {
  return roleOf(position) === 'GK' ? 65 : 74;
}

function clonePlayer(player) {
  return JSON.parse(JSON.stringify(player));
}

function createCanonicalPlayer(entry, team, currentPlayers, eaPlayers, countries, usedNumbers, allKnownPlayers) {
  const currentMatch = findBestEntryMatch(entry, currentPlayers, 0.56);
  if (currentMatch) {
    const player = clonePlayer(currentMatch.candidate);
    usedNumbers.add(player.squadNumber);
    return { player, source: 'current', matchedName: currentMatch.candidate.name };
  }

  const eaMatch = findBestEntryMatch(entry, eaPlayers, 0.56);
  const knownMatch = findBestEntryMatch(entry, allKnownPlayers, 0.68);
  const player = createFallbackPlayer(entry, team, eaMatch?.candidate, countries, usedNumbers, knownMatch?.candidate);

  return {
    player,
    source: eaMatch ? 'ea' : knownMatch ? 'known-player' : 'fallback',
    matchedName: eaMatch?.candidate.player ?? knownMatch?.candidate.name ?? ''
  };
}

function selectPlayers(team, official, eaPlayers, countries, allKnownPlayers) {
  const currentPlayers = allTeamPlayers(team);
  const usedNumbers = new Set();
  const selected = [];
  const additions = [];
  const retained = [];
  const omittedRegistered = [];
  const seenKeys = new Set();

  function addEntry(entry, group) {
    const canonicalEntry = { ...entry, group };
    const result = createCanonicalPlayer(canonicalEntry, team, currentPlayers, eaPlayers, countries, usedNumbers, allKnownPlayers);
    const key = normalizeKey(result.player.name);

    if (seenKeys.has(key)) {
      return null;
    }

    seenKeys.add(key);
    result.player.isStarter = false;
    selected.push({
      ...result,
      entry: canonicalEntry,
      group,
      currentIndex: currentPlayers.findIndex(player => normalizeKey(player.name) === normalizeKey(result.player.name)),
      currentStarter: team.startingXI.some(player => normalizeKey(player.name) === normalizeKey(result.player.name)),
      overall: result.player.overallRating ?? 0
    });

    if (result.source === 'current') {
      retained.push(result.player.name);
    } else {
      additions.push(result.player.name);
    }

    return result;
  }

  for (const entry of official.senior) {
    addEntry(entry, 'senior');
  }

  const seniorPlayers = [...selected];

  if (selected.length > maxPlayablePlayers) {
    selected.sort(compareSelectionPriority);
    const keep = new Set(selected.slice(0, maxPlayablePlayers).map(item => normalizeKey(item.player.name)));

    for (const item of selected.slice(maxPlayablePlayers)) {
      omittedRegistered.push(item.player.name);
    }

    selected.splice(0, selected.length, ...seniorPlayers.filter(item => keep.has(normalizeKey(item.player.name))));
  }

  const under21Candidates = official.under21
    .map(entry => {
      const currentMatch = findBestEntryMatch(entry, currentPlayers, 0.56);
      const eaMatch = findBestEntryMatch(entry, eaPlayers, 0.62);
      const currentIndex = currentMatch
        ? currentPlayers.findIndex(player => normalizeKey(player.name) === normalizeKey(currentMatch.candidate.name))
        : -1;
      const currentStarter = currentMatch
        ? team.startingXI.some(player => normalizeKey(player.name) === normalizeKey(currentMatch.candidate.name))
        : false;

      return {
        entry,
        currentMatch,
        eaMatch,
        currentIndex,
        currentStarter,
        priority: getPriorityPlayerScore(team.name, entry),
        overall: currentMatch?.candidate.overallRating ?? eaMatch?.candidate.overallRating ?? 0
      };
    })
    .filter(candidate => candidate.currentMatch || candidate.eaMatch)
    .sort((left, right) => {
      if (left.priority !== right.priority) {
        return right.priority - left.priority;
      }

      if (left.currentStarter !== right.currentStarter) {
        return left.currentStarter ? -1 : 1;
      }

      if ((left.currentIndex >= 0) !== (right.currentIndex >= 0)) {
        return left.currentIndex >= 0 ? -1 : 1;
      }

      if (left.currentIndex >= 0 && right.currentIndex >= 0 && left.currentIndex !== right.currentIndex) {
        return left.currentIndex - right.currentIndex;
      }

      return right.overall - left.overall;
    });

  for (const candidate of under21Candidates) {
    if (selected.length >= maxPlayablePlayers) {
      break;
    }

    if (candidate.currentMatch || selected.length < minPlayablePlayers) {
      addEntry(candidate.entry, 'under21');
    }
  }

  ensureBackupGoalkeeper(selected, official, team, currentPlayers, eaPlayers, countries, allKnownPlayers);

  selected.sort((left, right) => {
    const leftIndex = left.currentIndex < 0 ? 999 : left.currentIndex;
    const rightIndex = right.currentIndex < 0 ? 999 : right.currentIndex;
    return leftIndex - rightIndex || compareSelectionPriority(left, right);
  });

  const selectedKeys = new Set(selected.map(item => normalizeKey(item.player.name)));
  const removed = currentPlayers
    .filter(player => !selectedKeys.has(normalizeKey(player.name)))
    .map(player => player.name);

  return {
    selected,
    additions,
    retained,
    removed,
    omittedRegistered
  };
}

function getPriorityPlayerScore(teamName, entry) {
  if (normalizeKey(teamName) !== 'chelsea') {
    return 0;
  }

  return entry.candidateNames.some(name => normalizeKey(name) === 'estevao')
    ? 100
    : 0;
}

function compareSelectionPriority(left, right) {
  if (left.currentStarter !== right.currentStarter) {
    return left.currentStarter ? -1 : 1;
  }

  if ((left.currentIndex >= 0) !== (right.currentIndex >= 0)) {
    return left.currentIndex >= 0 ? -1 : 1;
  }

  if (roleOf(left.player.position) !== roleOf(right.player.position)) {
    const order = { GK: 0, DEF: 1, MID: 2, FWD: 3 };
    return order[roleOf(left.player.position)] - order[roleOf(right.player.position)];
  }

  return (right.overall ?? 0) - (left.overall ?? 0);
}

function ensureBackupGoalkeeper(selected, official, team, currentPlayers, eaPlayers, countries, allKnownPlayers) {
  const goalkeepers = selected.filter(item => roleOf(item.player.position) === 'GK');

  if (goalkeepers.length >= 2) {
    return;
  }

  const allEntries = [...official.senior.map(entry => ({ ...entry, group: 'senior' })), ...official.under21.map(entry => ({ ...entry, group: 'under21' }))];
  const existingKeys = new Set(selected.map(item => normalizeKey(item.player.name)));
  const usedNumbers = new Set(selected.map(item => item.player.squadNumber));

  for (const entry of allEntries) {
    const currentMatch = findBestEntryMatch(entry, currentPlayers, 0.56);
    const eaMatch = findBestEntryMatch(entry, eaPlayers, 0.56);
    const position = currentMatch?.candidate.position ?? eaMatch?.candidate.position;

    if (roleOf(position) !== 'GK') {
      continue;
    }

    const result = createCanonicalPlayer(entry, team, currentPlayers, eaPlayers, countries, usedNumbers, allKnownPlayers);
    const key = normalizeKey(result.player.name);

    if (existingKeys.has(key)) {
      continue;
    }

    const item = {
      ...result,
      entry,
      group: entry.group,
      currentIndex: currentPlayers.findIndex(player => normalizeKey(player.name) === normalizeKey(result.player.name)),
      currentStarter: false,
      overall: result.player.overallRating ?? 0
    };

    if (selected.length >= maxPlayablePlayers) {
      const replacementIndex = selected
        .map((candidate, index) => ({ candidate, index }))
        .filter(({ candidate }) => roleOf(candidate.player.position) !== 'GK' && !candidate.currentStarter)
        .sort((left, right) => (left.candidate.overall ?? 0) - (right.candidate.overall ?? 0))[0]?.index;

      if (replacementIndex == null) {
        return;
      }

      selected.splice(replacementIndex, 1, item);
    } else {
      selected.push(item);
    }

    return;
  }
}

function chooseStartingXI(team, selectedItems) {
  const selectedPlayers = selectedItems.map(item => item.player);
  for (const player of selectedPlayers) {
    player.isStarter = false;
  }

  const starters = [];
  const starterKeys = new Set();

  function addStarter(player) {
    const key = normalizeKey(player.name);
    if (starterKeys.has(key) || starters.length >= 11) {
      return false;
    }

    player.isStarter = true;
    starters.push(player);
    starterKeys.add(key);
    return true;
  }

  const currentStarterNames = new Set(team.startingXI.map(player => normalizeKey(player.name)));
  for (const item of selectedItems) {
    if (currentStarterNames.has(normalizeKey(item.player.name))) {
      addStarter(item.player);
    }
  }

  const roleTargets = { GK: 1, DEF: 4, MID: 3, FWD: 3 };

  for (const role of ['GK', 'DEF', 'MID', 'FWD']) {
    while (starters.filter(player => roleOf(player.position) === role).length < roleTargets[role]) {
      const candidate = selectedPlayers
        .filter(player => !starterKeys.has(normalizeKey(player.name)) && roleOf(player.position) === role)
        .sort((left, right) => (right.overallRating ?? 0) - (left.overallRating ?? 0))[0];

      if (!candidate) {
        break;
      }

      addStarter(candidate);
    }
  }

  while (starters.length < 11) {
    const candidate = selectedPlayers
      .filter(player => !starterKeys.has(normalizeKey(player.name)))
      .sort((left, right) => (right.overallRating ?? 0) - (left.overallRating ?? 0))[0];

    if (!candidate) {
      break;
    }

    addStarter(candidate);
  }

  const substitutes = selectedPlayers
    .filter(player => !starterKeys.has(normalizeKey(player.name)))
    .slice(0, 12);

  if (!substitutes.some(player => roleOf(player.position) === 'GK')) {
    const backupGoalkeeper = selectedPlayers
      .filter(player => !starterKeys.has(normalizeKey(player.name)) && roleOf(player.position) === 'GK')[0];

    if (backupGoalkeeper && substitutes.length > 0) {
      const replaceIndex = substitutes.findIndex(player => roleOf(player.position) !== 'GK');
      if (replaceIndex >= 0) {
        substitutes[replaceIndex] = backupGoalkeeper;
      }
    }
  }

  return { starters, substitutes };
}

function createLegacyPlayer(player, team, legacyTeamId) {
  return {
    id: player.playerId ?? `${legacyTeamId}-${slugify(player.name)}`,
    teamId: legacyTeamId,
    name: player.name,
    squadNumber: player.squadNumber,
    position: legacyPosition(player.position),
    preferredPosition: player.preferredPosition ?? player.position,
    secondaryPositions: player.secondaryPositions ?? [],
    nationality: player.nationality ?? null,
    nationalityCode: player.nationalityCode ?? null,
    nationalityName: player.nationalityName ?? null,
    flagEmoji: player.flagEmoji ?? null,
    flagImagePath: player.flagImagePath ?? null,
    overallRating: player.overallRating,
    age: player.age ?? null,
    potentialOverall: player.potentialOverall ?? null,
    pace: player.pace ?? null,
    shooting: player.shooting ?? null,
    passing: player.passing ?? null,
    dribbling: player.dribbling ?? null,
    defending: player.defending ?? null,
    physical: player.physical ?? null,
    stamina: player.stamina ?? null,
    isStarter: player.isStarter
  };
}

function legacyPosition(position) {
  const role = roleOf(position);
  if (role === 'GK') return 'Goalkeeper';
  if (role === 'DEF') return 'Defender';
  if (role === 'MID') return 'Midfielder';
  return 'Forward';
}

function legacyTeamId(team) {
  return (team.teamId || slugify(team.name)).replace(/^fc-/, '');
}

async function loadEaPlayers() {
  const players = [];

  for (const relativeUrl of eplTeamUrls) {
    const html = await fetchText(`${baseUrl}${relativeUrl}`);
    players.push(...parseRatingRows(html));
  }

  return players;
}

function validateTeam(team) {
  const allPlayers = allTeamPlayers(team);
  const numbers = new Set();

  if (team.startingXI.length !== 11) {
    throw new Error(`${team.name} must have exactly 11 starters.`);
  }

  if (team.substitutes.length < 7 || team.substitutes.length > 12) {
    throw new Error(`${team.name} must have 7-12 substitutes.`);
  }

  if (!team.startingXI.some(player => roleOf(player.position) === 'GK')) {
    throw new Error(`${team.name} must have a starting goalkeeper.`);
  }

  if (!team.substitutes.some(player => roleOf(player.position) === 'GK')) {
    throw new Error(`${team.name} must have a substitute goalkeeper.`);
  }

  for (const player of allPlayers) {
    if (!player.name || !player.nationalityCode || !player.nationalityName || !player.flagImagePath) {
      throw new Error(`${team.name} has incomplete player data for ${player.name}.`);
    }

    if (numbers.has(player.squadNumber)) {
      throw new Error(`${team.name} has duplicate squad number ${player.squadNumber}.`);
    }

    numbers.add(player.squadNumber);
  }
}

function sanitizePrimaryPositions(team) {
  for (const player of allTeamPlayers(team)) {
    const primary = normalizePrimaryPosition(player.preferredPosition ?? player.position);
    player.position = primary;
    player.preferredPosition = primary;
    player.secondaryPositions = (getKnownSecondaryPositions(player.name) ?? player.secondaryPositions ?? [])
      .map(normalizePrimaryPosition)
      .filter(position => position && position !== primary)
      .filter((position, index, positions) => positions.indexOf(position) === index);
  }
}

function normalizePrimaryPosition(position) {
  const exact = String(position ?? '').trim().toUpperCase();
  const map = {
    LM: 'LW',
    RM: 'RW'
  };
  const supported = new Set(['LW', 'ST', 'RW', 'CF', 'CM', 'CAM', 'CDM', 'LB', 'RB', 'CB', 'LWB', 'RWB', 'GK']);

  if (map[exact]) {
    return map[exact];
  }

  if (supported.has(exact)) {
    return exact;
  }

  return 'CM';
}

function ensureUniqueSquadNumbers(team) {
  const usedNumbers = new Set();

  for (const player of allTeamPlayers(team)) {
    if (
      Number.isInteger(player.squadNumber) &&
      player.squadNumber >= 1 &&
      player.squadNumber <= 99 &&
      !usedNumbers.has(player.squadNumber)
    ) {
      usedNumbers.add(player.squadNumber);
      continue;
    }

    player.squadNumber = nextSquadNumber(usedNumbers, player.position);
    usedNumbers.add(player.squadNumber);
  }
}

const squadsFile = JSON.parse(fs.readFileSync(squadsPath, 'utf8'));
const legacyPlayersFile = JSON.parse(fs.readFileSync(playersPath, 'utf8'));
const officialHtml = await fetchText(officialSquadsUrl);
const officialSquads = parseOfficialSquads(officialHtml);
const eaPlayers = await loadEaPlayers();
const countries = buildCountryMap(squadsFile);
const allKnownPlayers = squadsFile.teams.flatMap(team => allTeamPlayers(team));
const eaPlayersByTeam = new Map();
const legacyTeamIds = new Map();
const report = [];

for (const player of legacyPlayersFile.players ?? []) {
  const teamName = squadsFile.teams.find(team => legacyTeamId(team) === player.teamId)?.name;
  if (teamName && !legacyTeamIds.has(teamName)) {
    legacyTeamIds.set(teamName, player.teamId);
  }
}

for (const player of eaPlayers) {
  const key = normalizeKey(player.team);
  const list = eaPlayersByTeam.get(key) ?? [];
  list.push(player);
  eaPlayersByTeam.set(key, list);
}

for (const team of squadsFile.teams) {
  const official = officialSquads.get(team.name);
  if (!official) {
    throw new Error(`No official Premier League squad list found for ${team.name}.`);
  }

  const before = allTeamPlayers(team).map(player => player.name);
  const teamEaPlayers = eaPlayersByTeam.get(normalizeKey(team.name)) ?? [];
  const selection = selectPlayers(team, official, teamEaPlayers, countries, allKnownPlayers);
  const { starters, substitutes } = chooseStartingXI(team, selection.selected);

  team.startingXI = starters;
  team.substitutes = substitutes;

  sanitizePrimaryPositions(team);
  ensureUniqueSquadNumbers(team);
  validateTeam(team);

  const after = allTeamPlayers(team).map(player => player.name);
  report.push({
    team: team.name,
    officialSenior: official.senior.length,
    officialUnder21: official.under21.length,
    before: before.length,
    after: after.length,
    added: after.filter(name => !before.some(existing => normalizeKey(existing) === normalizeKey(name))),
    removed: selection.removed,
    omittedRegistered: selection.omittedRegistered
  });
}

const legacyPlayers = squadsFile.teams.flatMap(team => {
  const teamId = legacyTeamIds.get(team.name) ?? legacyTeamId(team);
  return allTeamPlayers(team).map(player => createLegacyPlayer(player, team, teamId));
});

const outputLegacy = {
  season: squadsFile.season,
  players: legacyPlayers
};

if (writeChanges) {
  squadsFile.sourceLastChecked = new Date().toISOString().slice(0, 10);
  fs.writeFileSync(squadsPath, `${JSON.stringify(squadsFile, null, 2)}\n`, 'utf8');
  fs.writeFileSync(playersPath, `${JSON.stringify(outputLegacy, null, 2)}\n`, 'utf8');
}

console.log(JSON.stringify({
  mode: writeChanges ? 'write' : 'dry-run',
  officialTeams: officialSquads.size,
  eaPlayers: eaPlayers.length,
  teams: report,
  totals: {
    teams: report.length,
    added: report.reduce((sum, team) => sum + team.added.length, 0),
    removed: report.reduce((sum, team) => sum + team.removed.length, 0),
    omittedRegistered: report.reduce((sum, team) => sum + team.omittedRegistered.length, 0),
    legacyPlayers: legacyPlayers.length
  }
}, null, 2));
