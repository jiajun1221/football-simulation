import { Activity, AlertCircle, Goal, LoaderCircle, Shield, Users } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';

const formatRating = (rating) => Number(rating ?? 0).toFixed(1);

function App() {
  const [teams, setTeams] = useState([]);
  const [selectedTeamName, setSelectedTeamName] = useState('');
  const [teamDetail, setTeamDetail] = useState(null);
  const [isLoadingTeams, setIsLoadingTeams] = useState(true);
  const [isLoadingDetail, setIsLoadingDetail] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    async function loadTeams() {
      try {
        const response = await fetch('/api/teams');
        if (!response.ok) {
          throw new Error('Teams could not be loaded.');
        }

        const data = await response.json();
        setTeams(data);
        setSelectedTeamName(data[0]?.name ?? '');
      } catch (loadError) {
        setError(loadError.message);
      } finally {
        setIsLoadingTeams(false);
      }
    }

    loadTeams();
  }, []);

  useEffect(() => {
    if (!selectedTeamName) {
      return;
    }

    async function loadTeamDetail() {
      setIsLoadingDetail(true);
      setError('');

      try {
        const response = await fetch(`/api/teams/${encodeURIComponent(selectedTeamName)}`);
        if (!response.ok) {
          throw new Error('Team details could not be loaded.');
        }

        setTeamDetail(await response.json());
      } catch (loadError) {
        setError(loadError.message);
      } finally {
        setIsLoadingDetail(false);
      }
    }

    loadTeamDetail();
  }, [selectedTeamName]);

  const selectedTeam = useMemo(
    () => teams.find((team) => team.name === selectedTeamName),
    [selectedTeamName, teams],
  );

  return (
    <main className="app-shell">
      <aside className="team-nav" aria-label="Premier League teams">
        <div className="brand-row">
          <img src="/football-logo.png" alt="" className="brand-logo" />
          <div>
            <p className="eyebrow">Football Simulation</p>
            <h1>Squad Dashboard</h1>
          </div>
        </div>

        {isLoadingTeams ? (
          <div className="status-line">
            <LoaderCircle size={18} className="spin" />
            Loading teams
          </div>
        ) : (
          <div className="team-list">
            {teams.map((team) => (
              <button
                className={team.name === selectedTeamName ? 'team-button active' : 'team-button'}
                key={team.name}
                onClick={() => setSelectedTeamName(team.name)}
                type="button"
              >
                <span>{team.name}</span>
                <strong>{formatRating(team.averageRating)}</strong>
              </button>
            ))}
          </div>
        )}
      </aside>

      <section className="content-area">
        {error ? (
          <div className="error-panel" role="alert">
            <AlertCircle size={22} />
            {error}
          </div>
        ) : null}

        {selectedTeam ? (
          <header className="club-header">
            <div>
              <p className="eyebrow">{selectedTeam.venue}</p>
              <h2>{selectedTeam.name}</h2>
              <p className="subtle-text">{selectedTeam.stadiumName}</p>
            </div>
            <div className="rating-badge">
              <span>Average</span>
              <strong>{formatRating(selectedTeam.averageRating)}</strong>
            </div>
          </header>
        ) : null}

        {isLoadingDetail ? (
          <div className="loading-block">
            <LoaderCircle size={26} className="spin" />
            Loading squad
          </div>
        ) : teamDetail ? (
          <>
            <section className="metric-grid" aria-label="Squad summary">
              <Metric icon={<Users size={20} />} label="Starters" value={teamDetail.team.starterCount} />
              <Metric icon={<Shield size={20} />} label="Substitutes" value={teamDetail.team.substituteCount} />
              <Metric icon={<Goal size={20} />} label="Formation" value={teamDetail.team.formation} />
              <Metric icon={<Activity size={20} />} label="Squad Rating" value={formatRating(teamDetail.team.averageRating)} />
            </section>

            <section className="dashboard-grid">
              <div className="pitch-panel">
                <div className="section-title">
                  <h3>Starting XI</h3>
                  <span>{teamDetail.team.formation}</span>
                </div>
                <div className="pitch">
                  {teamDetail.starters.map((player) => (
                    <div className="player-chip" key={player.playerId || player.name}>
                      <span className="number">{player.squadNumber || '-'}</span>
                      <span>{player.name}</span>
                      <strong>{player.position}</strong>
                    </div>
                  ))}
                </div>
              </div>

              <div className="squad-panel">
                <div className="section-title">
                  <h3>Squad List</h3>
                  <span>{teamDetail.starters.length + teamDetail.substitutes.length} players</span>
                </div>
                <PlayerTable title="Starters" players={teamDetail.starters} />
                <PlayerTable title="Bench" players={teamDetail.substitutes} />
              </div>
            </section>
          </>
        ) : null}
      </section>
    </main>
  );
}

function Metric({ icon, label, value }) {
  return (
    <div className="metric">
      <div className="metric-icon">{icon}</div>
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function PlayerTable({ title, players }) {
  return (
    <section className="player-section">
      <h4>{title}</h4>
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>No.</th>
              <th>Player</th>
              <th>Pos</th>
              <th>Nat</th>
              <th>OVR</th>
            </tr>
          </thead>
          <tbody>
            {players.map((player) => (
              <tr key={player.playerId || `${title}-${player.name}`}>
                <td>{player.squadNumber || '-'}</td>
                <td>{player.name}</td>
                <td>{player.position}</td>
                <td>{player.nationality || '-'}</td>
                <td>
                  <strong>{player.overallRating}</strong>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

export default App;
