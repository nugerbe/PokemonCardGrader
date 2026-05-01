// Sample data for the prototype
const SAMPLE_CARDS = [
  {
    id: 'ch-001', name: 'Aurora Drakon', subtitle: 'Holo · Prism Edition',
    set: 'Aetherfall', setCode: 'AET', number: '045/182',
    rarity: 'Rare Holo',
    // placeholder color for card-art
    hue: 220,
  },
  {
    id: 'ch-002', name: 'Moss Sentinel', subtitle: 'Full Art',
    set: 'Wildroot Vol. II', setCode: 'WRT',  number: '112/160',
    rarity: 'Ultra Rare',
    hue: 150,
  },
  {
    id: 'ch-003', name: 'Starwick Oracle', subtitle: 'Secret Rare',
    set: 'Solstice', setCode: 'SOL', number: '201/200',
    rarity: 'Secret Rare',
    hue: 280,
  },
  {
    id: 'ch-004', name: 'Emberwatch Fox', subtitle: '1st Edition',
    set: 'Coalveil', setCode: 'CVL', number: '019/120',
    rarity: 'Rare',
    hue: 25,
  },
  {
    id: 'ch-005', name: 'Tidecaller Lyr', subtitle: 'Reverse Holo',
    set: 'Foamline', setCode: 'FML', number: '078/150',
    rarity: 'Rare Holo',
    hue: 200,
  },
  {
    id: 'ch-006', name: 'Quartz Golem', subtitle: 'Alt Art',
    set: 'Deepcarve', setCode: 'DCV', number: '065/184',
    rarity: 'Ultra Rare',
    hue: 50,
  },
];

const SAMPLE_SUBMISSIONS = [
  {
    id: 'SUB-204A',
    card: SAMPLE_CARDS[0],
    date: 'Apr 18, 2026',
    status: 'Estimated',
    estimates: [
      { co: 'PSA',    grade: 9.0, label: 'Mint',  conf: 0.87, method: 'ML' },
      { co: 'BGS',    grade: 9.5, label: 'Gem Mint', conf: 0.79, method: 'ML' },
      { co: 'CGC',    grade: 9.0, label: 'Mint',  conf: 0.71, method: 'Rule-Based' },
      { co: 'SGC',    grade: 9.0, label: 'Mint',  conf: 0.68, method: 'Rule-Based' },
    ],
    actual: null,
    scores: { centerFLR: 52, centerFTB: 49, centerBLR: 55, centerBTB: 48, corners: 9.0, edges: 9.0, surface: 8.5 },
    sub: { Centering: 9.0, Corners: 9.0, Edges: 9.0, Surface: 8.5 },
  },
  {
    id: 'SUB-203F',
    card: SAMPLE_CARDS[1],
    date: 'Apr 16, 2026',
    status: 'Graded',
    estimates: [
      { co: 'PSA', grade: 10.0, label: 'Gem Mint', conf: 0.82, method: 'ML' },
    ],
    actual: { co: 'PSA', grade: 10, cert: '98234401' },
    scores: { centerFLR: 50, centerFTB: 50, centerBLR: 51, centerBTB: 50, corners: 10, edges: 10, surface: 9.5 },
  },
  {
    id: 'SUB-202B',
    card: SAMPLE_CARDS[2],
    date: 'Apr 12, 2026',
    status: 'Estimated',
    estimates: [
      { co: 'PSA', grade: 8.5, label: 'NM-Mint+', conf: 0.74, method: 'ML' },
      { co: 'BGS', grade: 8.5, label: 'NM-Mint+', conf: 0.69, method: 'ML' },
    ],
    actual: null,
  },
  {
    id: 'SUB-201C',
    card: SAMPLE_CARDS[3],
    date: 'Apr 09, 2026',
    status: 'Graded',
    estimates: [
      { co: 'PSA', grade: 9.0, label: 'Mint', conf: 0.81, method: 'ML' },
    ],
    actual: { co: 'PSA', grade: 9, cert: '98221100' },
  },
  {
    id: 'SUB-199D',
    card: SAMPLE_CARDS[4],
    date: 'Apr 06, 2026',
    status: 'Estimated',
    estimates: [
      { co: 'PSA', grade: 7.5, label: 'Near Mint+', conf: 0.66, method: 'ML' },
    ],
    actual: null,
  },
  {
    id: 'SUB-197A',
    card: SAMPLE_CARDS[5],
    date: 'Apr 02, 2026',
    status: 'Graded',
    estimates: [
      { co: 'PSA', grade: 9.5, label: 'Gem Mint-', conf: 0.78, method: 'ML' },
    ],
    actual: { co: 'BGS', grade: 9.5, cert: '0015599221' },
  },
];

const GRADE_DIST = [
  { g: 10, n: 4 },
  { g: 9.5, n: 7 },
  { g: 9,  n: 18 },
  { g: 8.5, n: 12 },
  { g: 8, n: 8 },
  { g: 7.5, n: 3 },
  { g: 7, n: 2 },
];

Object.assign(window, { SAMPLE_CARDS, SAMPLE_SUBMISSIONS, GRADE_DIST });
