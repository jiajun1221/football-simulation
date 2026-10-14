import fs from 'node:fs';
import path from 'node:path';

const badges = {
  'aek-athens':'AEK_Athens_F.C.', 'bodo-glimt':'FK_Bodø/Glimt',
  'deportivo-la-coruna':'Deportivo_de_La_Coruña', 'elversberg':'SV_Elversberg',
  'fenerbahce':'Fenerbahçe_S.K._(football)', 'frosinone':'Frosinone_Calcio',
  'hull-city':'Hull_City_A.F.C.', 'lask':'LASK', 'le-mans':'Le_Mans_FC',
  'malaga':'Málaga_CF', 'monza':'AC_Monza', 'paderborn':'SC_Paderborn_07',
  'racing-santander':'Racing_de_Santander', 'sabah':'Sabah_FC_(Azerbaijan)',
  'schalke-04':'FC_Schalke_04', 'slavia-prague':'SK_Slavia_Prague',
  'slovan-bratislava':'ŠK_Slovan_Bratislava', 'troyes':'ES_Troyes_AC',
  'venezia':'Venezia_FC', 'viking':'Viking_FK'
};

const outputDirectory = 'src/FootballSimulation.Wpf/Assets/Clubs';
const wait = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
async function fetchWithRetry(url) {
  for (let attempt = 1; attempt <= 5; attempt++) {
    const response = await fetch(url, { headers:{ 'user-agent':'FootballSimulationSquadUpdater/1.0 (local development)' } });
    if (response.ok || response.status !== 429) return response;
    await wait(attempt * 1500);
  }
  return fetch(url, { headers:{ 'user-agent':'FootballSimulationSquadUpdater/1.0 (local development)' } });
}

for (const [slug,title] of Object.entries(badges)) {
  const outputPath = path.join(outputDirectory, `${slug}.png`);
  if (fs.existsSync(outputPath)) continue;
  const summaryUrl = `https://en.wikipedia.org/api/rest_v1/page/summary/${encodeURIComponent(title)}`;
  const response = await fetchWithRetry(summaryUrl);
  if (!response.ok) throw new Error(`${response.status} ${summaryUrl}`);
  const summary = await response.json();
  const imageUrl = summary.thumbnail?.source;
  if (!imageUrl) throw new Error(`No badge thumbnail for ${title}`);
  const imageResponse = await fetchWithRetry(imageUrl);
  if (!imageResponse.ok) throw new Error(`${imageResponse.status} ${imageUrl}`);
  fs.writeFileSync(outputPath, Buffer.from(await imageResponse.arrayBuffer()));
  await wait(500);
}

console.log(`Downloaded ${Object.keys(badges).length} club badges.`);
