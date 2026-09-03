import fs from 'node:fs';
import path from 'node:path';

const writeChanges = process.argv.includes('--write');
const argument = name => process.argv.find(value => value.startsWith(`--${name}=`))?.slice(name.length + 3);
const rosterPath = argument('roster');
const transfersPath = argument('transfers');
if (!rosterPath || !transfersPath) {
  throw new Error('Usage: node import-attached-2026-27-rosters.mjs --roster="...txt" --transfers="...txt" [--write]');
}

const dataDirectory = 'src/FootballSimulation.Core/Data/Json';
const season = '2026-27';
const domesticFiles = {
  'premier-league':'premier-league-2026-27-squads.json',
  'la-liga':'la-liga-2026-27-squads.json',
  'serie-a':'serie-a-2026-27-squads.json',
  'bundesliga':'bundesliga-2026-27-squads.json',
  'ligue-1':'ligue-1-2026-27-squads.json'
};
const championsLeagueFile = 'champions-league-2026-27-squads.json';
const leagueNames = {
  'PREMIER LEAGUE':'premier-league', 'LA LIGA':'la-liga', 'SERIE A':'serie-a',
  'BUNDESLIGA':'bundesliga', 'LIGUE 1':'ligue-1'
};
const teamAliases = new Map(Object.entries({
  'atletico de madrid':'Atletico Madrid', 'deportivo a coruna':'Deportivo La Coruna',
  'fc barcelona':'Barcelona', 'inter':'Inter Milan', 'bayer 04 leverkusen':'Bayer Leverkusen',
  'fc augsburg':'Augsburg', 'fc bayern munchen':'Bayern Munich', 'fc koln':'FC Koln',
  'fc union berlin':'Union Berlin', 'fsv mainz 05':'Mainz 05', 'hamburger sv':'Hamburg',
  'sc freiburg':'Freiburg', 'vfb stuttgart':'Stuttgart', 'angers sco':'Angers',
  'losc lille':'Lille', 'olympique lyonnais':'Lyon', 'olympique marseille':'Marseille',
  'paris':'Paris FC', 'paris saint germain':'Paris Saint-Germain', 'brighton':'Brighton & Hove Albion',
  'bournemouth':'AFC Bournemouth', 'newcastle':'Newcastle United', 'manchester utd':'Manchester United',
  'man utd':'Manchester United', 'tottenham':'Tottenham Hotspur', 'col...':'FC Koln', 'cologne':'FC Koln',
  'celta de vigo':'Celta Vigo', 'tsg hoffenheim':'Hoffenheim', 'dortmund':'Borussia Dortmund'
}));
for (const file of Object.values(domesticFiles)) {
  const data = JSON.parse(fs.readFileSync(path.join(dataDirectory, file), 'utf8'));
  for (const team of data.teams ?? []) if (!teamAliases.has(normalize(team.name))) teamAliases.set(normalize(team.name), team.name);
}
for (const team of JSON.parse(fs.readFileSync(path.join(dataDirectory, championsLeagueFile), 'utf8')).teams ?? []) {
  teamAliases.set(normalize(team.name), team.name);
}
teamAliases.set('lecce', 'Lecce');
const positionAliases = new Map(Object.entries({
  GK:'GK', CB:'CB', LB:'LB', RB:'RB', WB:'RB', DM:'CDM', CM:'CM', AM:'CAM',
  LM:'LM', RM:'RM', LW:'LW', RW:'RW', CF:'CF', ST:'ST'
}));

// Verified overrides for players whose attached roster rows contain TBC values and
// who are not present in the five-league rating pool. EA values are preferred;
// documented academy players without an FC 27 entry use a conservative estimate.
const knownPlayerOverrides = new Map([
  ['caleb wiley', { ...verifiedPlayer('ea:266605', 'LB', 'United States', 'US', 21, 68,
    { pace:76, shooting:51, passing:59, dribbling:70, defending:61, physical:64 }),
    preferredFoot:'Left', potentialOverall:81, secondaryPositions:['LWB','LM'] }],
  ['dastan satpayev', { ...verifiedPlayer('profile:dastan-satpayev:2008-08-12:kazakhstan', 'ST', 'Kazakhstan', 'KZ', 18, 63), potentialOverall:81 }],
  ['denner', { ...verifiedPlayer('profile:denner-alves-evangelista-pereira:2008-02-25:brazil', 'LB', 'Brazil', 'BR', 18, 66),
    preferredFoot:'Left', potentialOverall:82, secondaryPositions:['LWB'] }],
  ['teddy sharman lowe', { ...verifiedPlayer('ea:257126', 'GK', 'England', 'EN', 23, 64,
    { pace:40, shooting:10, passing:35, dribbling:28, defending:15, physical:62 }), potentialOverall:74 }],
  ['harrison murray campbell', verifiedPlayer('ea:87308', 'CB', 'England', 'EN', 20, 64, { pace:60, shooting:43, passing:42, dribbling:55, defending:65, physical:68 })],
  ['ishe samuels smith', verifiedPlayer('ea:275244', 'LB', 'England', 'EN', 20, 68, { pace:75, shooting:43, passing:60, dribbling:62, defending:63, physical:66 })],
  ['kaiden wilson', verifiedPlayer('ea:78114', 'CB', 'England', 'EN', 20, 59, { pace:72, shooting:29, passing:42, dribbling:48, defending:58, physical:69 })],
  ['ollie harrison', verifiedPlayer('ea:71126', 'CDM', 'England', 'EN', 19, 60, { pace:60, shooting:46, passing:62, dribbling:60, defending:55, physical:58 })],
  ['dujuan richards', verifiedPlayer('profile:dujuan-richards:2005-11-10:jamaica', 'ST', 'Jamaica', 'JM', 20, 64)],
  ['reggie walsh', verifiedPlayer('profile:reggie-walsh:2008-10-20:england', 'CAM', 'England', 'EN', 17, 62)],
  ['ryan kavuma mcqueen', verifiedPlayer('profile:ryan-kavuma-mcqueen:2009-01-01:england', 'LW', 'England', 'EN', 17, 62)]
]);

function verifiedPlayer(playerId, position, nationalityName, nationalityCode, age, overallRating, attributes = fallbackAttributes(overallRating, position)) {
  return { playerId, position, preferredPosition:position, nationalityName, nationality:nationalityName,
    nationalityCode, flagImagePath:`/Assets/Flags/${slug(nationalityName)}.png`, age, overallRating,
    potentialOverall:Math.min(88, overallRating + (age <= 18 ? 10 : age <= 21 ? 7 : 3)), ...attributes,
    stamina:Math.max(60, overallRating), traits:[], morale:50, preferredFoot:'Right', secondaryPositions:commonSecondaryPositions(position) };
}

function normalize(value) {
  return String(value ?? '').replace(/[øØ]/g,'o').replace(/[łŁ]/g,'l').replace(/[đĐ]/g,'d').replace(/[ðÐ]/g,'d')
    .normalize('NFD').replace(/\p{Diacritic}/gu, '')
    .replace(/&/g, ' and ').replace(/[^a-z0-9]+/gi, ' ').trim().toLowerCase();
}
function editDistance(first,second){const a=normalize(first),b=normalize(second),row=Array.from({length:b.length+1},(_,i)=>i);
  for(let i=1;i<=a.length;i++){let diagonal=row[0];row[0]=i;for(let j=1;j<=b.length;j++){const above=row[j];row[j]=Math.min(row[j]+1,row[j-1]+1,diagonal+(a[i-1]===b[j-1]?0:1));diagonal=above;}}return row[b.length];}
function slug(value) { return normalize(value).replace(/\s+/g, '-'); }
function canonicalTeam(value) {
  const key = normalize(value);
  return teamAliases.get(key) ?? String(value).trim().toLowerCase().replace(/(^|\s|-)\p{L}/gu, character => character.toUpperCase());
}
function parsePlayerRow(line) {
  const match = line.match(/^\s*(\d+|[-—])\s*\|\s*([^|]+?)\s*\|\s*([^|]+?)\s*\|\s*([^|]+?)\s*\|\s*Age\s*(\d+|-)(?:\s*\|\s*To:\s*(.+))?\s*$/u);
  if (!match) return null;
  return {
    squadNumber: /^\d+$/.test(match[1]) ? Number(match[1]) : 0,
    name: match[2].trim(), position: match[3].trim(), nationality: match[4].trim(),
    age: /^\d+$/.test(match[5]) ? Number(match[5]) : null, destination: match[6]?.trim() ?? ''
  };
}

function parseRoster(text) {
  const lines = text.split(/\r?\n/);
  const leagues = new Map();
  const loanMoves = [];
  let leagueId = '';
  let teamName = '';
  let mode = '';
  for (let index = 0; index < lines.length; index++) {
    const line = lines[index].trimEnd();
    if (leagueNames[line.trim()]) { leagueId = leagueNames[line.trim()]; teamName = ''; continue; }
    if (line === 'DEADLINE-DAY CORRECTION APPENDIX') break;
    if (lines[index + 1]?.trim().match(/^-{3,}$/) && line.trim()) {
      teamName = canonicalTeam(line.trim());
      if (!leagues.has(leagueId)) leagues.set(leagueId, new Map());
      leagues.get(leagueId).set(teamName, []);
      mode = '';
      continue;
    }
    if (/^Published roster \(/.test(line.trim())) { mode = 'active'; continue; }
    if (/^OUT ON LOAN/.test(line.trim())) { mode = 'loan'; continue; }
    if (/^Source:/.test(line.trim())) { mode = ''; continue; }
    if (!teamName || !mode) continue;
    const player = parsePlayerRow(line);
    if (!player) continue;
    if (mode === 'active') leagues.get(leagueId).get(teamName).push(player);
    if (mode === 'loan') loanMoves.push({ playerName:player.name, parentTeam:teamName, destination:canonicalTeam(player.destination), row:player });
  }
  return { leagues, loanMoves };
}

function parseTransferList(text, premierLeagueTeams) {
  const lines = text.split(/\r?\n/);
  const moves = [];
  let currentTeam = '';
  let mode = '';
  let inEuropeanAppendix = false;
  for (let index = 0; index < lines.length; index++) {
    const line = lines[index].trim();
    if (line === 'DEADLINE-DAY EUROPEAN MOVES INVOLVING NON-PL CLUBS') { inEuropeanAppendix = true; currentTeam = ''; mode = ''; continue; }
    if (premierLeagueTeams.has(canonicalTeam(line)) && lines[index + 1]?.includes('====')) { currentTeam = canonicalTeam(line); mode = ''; inEuropeanAppendix = false; continue; }
    if (line === 'IN' || line === 'OUT') { mode = line; continue; }
    if (!line || line.includes('====') || line === 'SOURCE') continue;
    const parts = line.split(/\s+—\s+/u).map(value => value.trim());
    if (inEuropeanAppendix && parts.length >= 3) {
      const route = parts[1].match(/^(.+?)\s+to\s+(.+)$/i);
      if (route) moves.push({ playerName:parts[0], source:canonicalTeam(route[1]), destination:canonicalTeam(route[2]), isLoan:/\bLoan\b/i.test(parts[2]), detail:parts[2] });
      continue;
    }
    if (!currentTeam || !mode || parts.length < 3) continue;
    const otherTeam = canonicalTeam(parts[1]);
    moves.push({ playerName:parts[0], source:mode === 'OUT' ? currentTeam : otherTeam,
      destination:mode === 'OUT' ? otherTeam : currentTeam, isLoan:/\bLoan\b/i.test(parts[2]), detail:parts[2] });
  }
  return moves.filter((move, index, all) => index === all.findIndex(other =>
    normalize(other.playerName) === normalize(move.playerName) && normalize(other.source) === normalize(move.source) && normalize(other.destination) === normalize(move.destination)));
}

function loadRatingPool() {
  const players = [];
  for (const file of fs.readdirSync(dataDirectory).filter(file => /202(5-26|6-27)-squads\.json$/.test(file))) {
    const data = JSON.parse(fs.readFileSync(path.join(dataDirectory, file), 'utf8'));
    for (const team of data.teams ?? []) for (const group of ['startingXI','substitutes','reserves']) {
      for (const player of team[group] ?? []) players.push({ ...player, sourceTeam:team.name });
    }
  }
  return players;
}

function positionGroup(position) {
  if (position === 'GK') return 'GK';
  if (['CB','LB','RB'].includes(position)) return 'D';
  if (['CDM','CM','CAM','LM','RM'].includes(position)) return 'M';
  return 'F';
}
function chooseRating(row, pool) {
  const wantedName=normalize(row.name);
  const verified = knownPlayerOverrides.get(wantedName);
  if (verified) return verified;
  const wantedSurname=wantedName.split(' ').at(-1);
  let matches = pool.filter(player => {
    const candidateName=normalize(player.name);
    const candidateParts=candidateName.split(' '),wantedParts=wantedName.split(' ');
    return candidateName===wantedName || (candidateParts.at(-1)===wantedSurname &&
      editDistance(candidateParts[0],wantedParts[0])<=1 && editDistance(candidateName,wantedName)<=2);
  });
  const expectedPosition = positionAliases.get(String(row.position ?? '').toUpperCase());
  return matches.sort((a,b) => {
    const aScore = (expectedPosition && a.position === expectedPosition ? 5000 : 0) + (row.age && a.age === row.age ? 1000 : 0) + (row.nationality && normalize(a.nationalityName) === normalize(row.nationality) ? 1000 : 0) + (a.playerId?.startsWith('ea:') ? 5000 : a.playerId ? 1000 : 0);
    const bScore = (expectedPosition && b.position === expectedPosition ? 5000 : 0) + (row.age && b.age === row.age ? 1000 : 0) + (row.nationality && normalize(b.nationalityName) === normalize(row.nationality) ? 1000 : 0) + (b.playerId?.startsWith('ea:') ? 5000 : b.playerId ? 1000 : 0);
    return bScore - aScore;
  })[0];
}
function fallbackAttributes(overall, position) {
  const group = positionGroup(position);
  return { pace:Math.min(90,overall+(group === 'F'?5:1)), shooting:Math.max(30,overall+(group === 'F'?1:-18)),
    passing:Math.max(40,overall+(group === 'M'?1:-8)), dribbling:Math.max(40,overall+(group === 'F'?2:-7)),
    defending:Math.max(25,overall+(group === 'D'?1:-20)), physical:Math.max(45,overall) };
}
function commonSecondaryPositions(position) {
  return ({LB:['LWB'],RB:['RWB'],LWB:['LB','LM'],RWB:['RB','RM'],LW:['LM'],RW:['RM'],
    LM:['LW','LWB'],RM:['RW','RWB'],CAM:['LM','RM']}[position] ?? []);
}
function createPlayer(row, teamName, ratingPool, fallbacks) {
  const source = chooseRating(row, ratingPool);
  const suppliedPosition = String(row.position ?? '').toUpperCase();
  const position = suppliedPosition === 'TBC' || suppliedPosition === '-'
    ? source?.position ?? 'CM'
    : positionAliases.get(suppliedPosition) ?? suppliedPosition;
  const nationality = row.nationality === '-' || normalize(row.nationality) === 'tbc' ? source?.nationalityName ?? 'Unknown' : row.nationality;
  if (source) {
    if (source.playerId?.startsWith('attached:')) fallbacks.push({team:teamName,player:row.name,reason:'Previous conservative fallback retained; no EA identity match'});
    return { ...source, playerId:source.playerId || `attached:${slug(row.name)}:${row.age ?? source.age ?? 'unknown'}:${slug(nationality)}`,
    name:row.name, squadNumber:row.squadNumber || source.squadNumber, position,
    preferredPosition:position, secondaryPositions:[...new Set([...(source.secondaryPositions??[]),...commonSecondaryPositions(position)])].filter(value=>value!==position), age:row.age ?? source.age,
    nationalityName:nationality, nationality,
    nationalityCode:source.nationalityCode || nationality.slice(0,2).toUpperCase(),
    flagImagePath:source.flagImagePath || `/Assets/Flags/${slug(nationality)}.png`,
    isOnLoan:false, parentClubId:'', parentClubName:'', loanClubName:'', loanEndSeason:'', loanWagePercentage:0,
    sourceTeam:undefined };
  }
  const overall = 66;
  const attributes = fallbackAttributes(overall, position);
  fallbacks.push({ team:teamName, player:row.name, reason:'No EA/previous-season identity match; conservative role median used' });
  return { playerId:`attached:${slug(row.name)}:${row.age ?? 'unknown'}:${slug(row.nationality)}`, name:row.name,
    squadNumber:row.squadNumber, position, preferredPosition:position, secondaryPositions:commonSecondaryPositions(position), preferredFoot:'Right',
    nationalityCode:(row.nationality || 'XX').slice(0,2).toUpperCase(), nationalityName:row.nationality || 'Unknown',
    nationality:row.nationality || 'Unknown', flagImagePath:`/Assets/Flags/${slug(row.nationality || 'unknown')}.png`,
    overallRating:overall, age:row.age ?? 21, potentialOverall:Math.min(84,overall + ((row.age ?? 21) <= 21 ? 6 : 2)),
    ...attributes, stamina:72, traits:[], morale:50, isOnLoan:false, parentClubId:'', parentClubName:'',
    loanClubName:'', loanEndSeason:'', loanWagePercentage:0 };
}
function takeBest(remaining, group, count) {
  const selected = remaining.filter(player => positionGroup(player.position) === group).sort((a,b) => b.overallRating-a.overallRating).slice(0,count);
  for (const player of selected) remaining.splice(remaining.indexOf(player),1);
  return selected;
}
function partition(players) {
  const remaining = [...players];
  const startingXI = [...takeBest(remaining,'GK',1),...takeBest(remaining,'D',4),...takeBest(remaining,'M',3),...takeBest(remaining,'F',3)];
  while(startingXI.length<11 && remaining.length) startingXI.push(remaining.sort((a,b)=>b.overallRating-a.overallRating).shift());
  const substitutes=[...takeBest(remaining,'GK',1),...takeBest(remaining,'D',3),...takeBest(remaining,'M',3),...takeBest(remaining,'F',2)];
  while(substitutes.length<12 && remaining.length) substitutes.push(remaining.sort((a,b)=>b.overallRating-a.overallRating).shift());
  return {startingXI,substitutes,reserves:remaining.sort((a,b)=>b.overallRating-a.overallRating)};
}
function assignNumbers(players) {
  const used=new Set();
  for(const player of players){if(player.squadNumber>0&&player.squadNumber<=99&&!used.has(player.squadNumber)){used.add(player.squadNumber);continue;}
    player.squadNumber=Array.from({length:99},(_,i)=>i+1).find(number=>!used.has(number));used.add(player.squadNumber);}
}

const rosterText = fs.readFileSync(rosterPath,'utf8');
const transferText = fs.readFileSync(transfersPath,'utf8');
const parsed = parseRoster(rosterText);
const premierLeagueTeams = new Set(parsed.leagues.get('premier-league').keys());
const transferMoves = parseTransferList(transferText,premierLeagueTeams);
const ratingPool = loadRatingPool();
const fallbacks=[];
const activeTeams = new Set([...parsed.leagues.values()].flatMap(teams=>[...teams.keys()]));

// Reconcile the attached transfer ledger after the published-roster snapshot.
for(const move of transferMoves){
  for(const teams of parsed.leagues.values()) for(const [teamName,rows] of teams) {
    if(normalize(teamName)===normalize(move.source)) teams.set(teamName,rows.filter(row=>normalize(row.name)!==normalize(move.playerName)));
  }
  if(!activeTeams.has(move.destination)) continue;
  const targetLeague=[...parsed.leagues].find(([,teams])=>teams.has(move.destination));
  if(!targetLeague) continue;
  const targetRows=targetLeague[1].get(move.destination);
  if(!targetRows.some(row=>normalize(row.name)===normalize(move.playerName))) {
    const sourceRating=chooseRating({name:move.playerName},ratingPool);
    targetRows.push({name:move.playerName,squadNumber:sourceRating?.squadNumber??0,position:sourceRating?.position??'CM',
      nationality:sourceRating?.nationalityName??'Unknown',age:sourceRating?.age??null});
  }
}

const loanMoves=[...parsed.loanMoves,...transferMoves.filter(move=>move.isLoan).map(move=>({playerName:move.playerName,parentTeam:move.source,destination:move.destination}))];
const loanByPlayer=new Map(loanMoves.map(move=>[normalize(move.playerName),move]));
const outputs=[];
for(const [leagueId,teams] of parsed.leagues){
  const previous=JSON.parse(fs.readFileSync(path.join(dataDirectory,domesticFiles[leagueId]),'utf8'));
  const generatedTeams=[];
  for(const [teamName,rows] of teams){
    const uniqueRows=rows.filter((row,index)=>row.position!=='TBC'&&index===rows.findIndex(other=>normalize(other.name)===normalize(row.name)));
    const players=uniqueRows.map(row=>createPlayer(row,teamName,ratingPool,fallbacks));
    for(const player of players){const loan=loanByPlayer.get(normalize(player.name));if(loan&&normalize(loan.destination)===normalize(teamName)){
      player.isOnLoan=true;player.parentClubName=loan.parentTeam;player.parentClubId=slug(loan.parentTeam);player.loanEndSeason=season;player.loanWagePercentage=100;
      player.loanClubName=teamName;
    }}
    assignNumbers(players);
    const squad=partition(players);
    if(squad.startingXI.length!==11||squad.substitutes.length<7||squad.substitutes.length>12||!squad.startingXI.some(player=>player.position==='GK')||!squad.substitutes.some(player=>player.position==='GK')) throw new Error(`${teamName} cannot form a valid matchday squad (${players.length} players).`);
    const oldTeam=previous.teams.find(team=>normalize(team.name)===normalize(teamName));
    const loanedOut=loanMoves.filter(move=>normalize(move.parentTeam)===normalize(teamName)).map(move=>{
      const source=chooseRating({name:move.playerName},ratingPool);
      const row=move.row??{name:move.playerName,squadNumber:source?.squadNumber??0,position:source?.position??'CM',nationality:source?.nationalityName??'Unknown',age:source?.age??null};
      const player=createPlayer(row,teamName,ratingPool,fallbacks);
      player.isOnLoan=true;player.parentClubName=teamName;player.parentClubId=slug(teamName);player.loanClubName=move.destination;
      player.loanEndSeason=season;player.loanWagePercentage=100;return player;
    }).filter((player,index,all)=>index===all.findIndex(other=>normalize(other.name)===normalize(player.name)));
    generatedTeams.push({teamId:slug(teamName),name:teamName,shortName:oldTeam?.shortName??teamName,city:oldTeam?.city??'',logoKey:slug(teamName),
      venue:oldTeam?.venue??`${teamName} Stadium`,stadiumName:oldTeam?.stadiumName??oldTeam?.venue??`${teamName} Stadium`,formation:oldTeam?.formation??'4-3-3',...squad,loanedOut});
  }
  outputs.push({file:domesticFiles[leagueId],data:{leagueId,leagueName:previous.leagueName,season,sourceLastChecked:'2026-09-02',
    sources:[path.basename(rosterPath),path.basename(transfersPath),'https://www.ea.com/games/ea-sports-fc/ratings'],teams:generatedTeams}});
}

// Reconcile transfers involving the 15 supplemental Champions League clubs, then
// remove any identity now owned by a domestic club.
const domesticIds = new Set(outputs.flatMap(output => output.data.teams)
  .flatMap(team => [...team.startingXI,...team.substitutes,...team.reserves]).map(player => player.playerId));
const previousChampionsLeague = JSON.parse(fs.readFileSync(path.join(dataDirectory, championsLeagueFile), 'utf8'));
const championsLeagueTeams = previousChampionsLeague.teams.map(team => {
  const outboundNames = new Set(transferMoves.filter(move => normalize(move.source) === normalize(team.name)).map(move => normalize(move.playerName)));
  let players = [...team.startingXI,...team.substitutes,...team.reserves]
    .filter(player => !outboundNames.has(normalize(player.name)) && !domesticIds.has(player.playerId));
  for (const move of transferMoves.filter(move => normalize(move.destination) === normalize(team.name))) {
    if (players.some(player => normalize(player.name) === normalize(move.playerName))) continue;
    const source = chooseRating({name:move.playerName}, ratingPool);
    const player = createPlayer({name:move.playerName,squadNumber:source?.squadNumber??0,position:source?.position??'CM',
      nationality:source?.nationalityName??'Unknown',age:source?.age??null},team.name,ratingPool,fallbacks);
    if(move.isLoan){player.isOnLoan=true;player.parentClubName=move.source;player.parentClubId=slug(move.source);
      player.loanClubName=team.name;player.loanEndSeason=season;player.loanWagePercentage=100;}
    players.push(player);
  }
  assignNumbers(players);
  const squad=partition(players);
  if(squad.startingXI.length!==11||squad.substitutes.length<7||!squad.startingXI.some(player=>player.position==='GK')||!squad.substitutes.some(player=>player.position==='GK')) {
    throw new Error(`${team.name} cannot form a valid Champions League squad after transfer reconciliation (${players.length} players).`);
  }
  return {...team,...squad};
});
outputs.push({file:championsLeagueFile,data:{...previousChampionsLeague,season,sourceLastChecked:'2026-09-02',
  sources:[...(previousChampionsLeague.sources??[]),path.basename(transfersPath)],teams:championsLeagueTeams}});

const ownership=new Map();
for(const output of outputs)for(const team of output.data.teams)for(const player of [...team.startingXI,...team.substitutes,...team.reserves]){
  const existing=ownership.get(player.playerId);if(existing&&existing!==team.name)throw new Error(`${player.name} (${player.playerId}) appears at ${existing} and ${team.name}.`);ownership.set(player.playerId,team.name);
}
if(writeChanges)for(const output of outputs)fs.writeFileSync(path.join(dataDirectory,output.file),`${JSON.stringify(output.data,null,2)}\n`);
console.log(JSON.stringify({mode:writeChanges?'write':'dry-run',clubs:outputs.reduce((sum,output)=>sum+output.data.teams.length,0),
  players:ownership.size,loans:[...loanByPlayer.values()].filter(loan=>activeTeams.has(loan.destination)).length,fallbackCount:fallbacks.length,fallbacks},null,2));
