const CLOWN_SIZE = 64;
const CLOWN_DEATH_CLICKS = 500;
const MS_PER_FRAME = 30;

const clown = document.querySelector(".clown");
const horn = document.querySelector(".bike-horn");
const counter = document.querySelector(".counter");
const clickCount = document.querySelector(".click-count");
const taunt = document.querySelector(".taunt");

const position = { x: -CLOWN_SIZE, y: -CLOWN_SIZE };
let velocity = { x: 1, y: 1 };
let timesClicked = 0;
let clownIsAlive = true;
let hornVolume = 0.25;

window.wallpaperPropertyListener = {
  applyUserProperties(properties) {
    if (properties.horn_volume) {
      hornVolume = properties.horn_volume.value;
    }
  },
};

function randomVelocity() {
  const angle = Math.random() * Math.PI * 2;
  const speed = 1.5 + Math.random() * 3.5;
  return {
    x: Math.cos(angle) * speed,
    y: Math.sin(angle) * speed,
  };
}

function tauntFor(count) {
  if (count > 499) return "Okay, you killed the clown. I hope you are happy now.";
  if (count > 300) return "What are you trying to prove?";
  if (count > 200) return "Seriously?";
  if (count > 100) return "Don't you have anything better to do?";
  if (count > 50) return "Aren't you getting bored?";
  if (count > 20) return "Clicking the clown is still funny, apparently.";
  return "";
}

function placeClown() {
  clown.style.transform = `translate3d(${position.x}px, ${position.y}px, 0)`;
}

function honk() {
  horn.muted = false;
  horn.volume = hornVolume;
  horn.currentTime = 0;
  void horn.play().catch((error) => console.error("Could not play the bike horn:", error));
}

clown.addEventListener("click", () => {
  honk();
  timesClicked += 1;
  velocity = randomVelocity();

  clickCount.textContent = `Times you have clicked the clown: ${timesClicked}`;
  taunt.textContent = tauntFor(timesClicked);
  taunt.hidden = !taunt.textContent;
  counter.hidden = timesClicked <= 5;

  if (timesClicked >= CLOWN_DEATH_CLICKS) {
    clownIsAlive = false;
    clown.remove();
  }
});

if (matchMedia("(prefers-reduced-motion: reduce)").matches) {
  position.x = CLOWN_SIZE / 2;
  position.y = innerHeight - CLOWN_SIZE * 1.5;
  placeClown();
} else {
  let previousTime = performance.now();

  function animate(time) {
    if (!clownIsAlive) return;

    const frames = Math.min((time - previousTime) / MS_PER_FRAME, 3);
    previousTime = time;

    const nextX = position.x + velocity.x * frames;
    const nextY = position.y + velocity.y * frames;
    if (nextX > innerWidth + CLOWN_SIZE / 4 || nextX < -CLOWN_SIZE * 2) {
      velocity.x *= -1;
    }
    if (nextY > innerHeight + CLOWN_SIZE / 4 || nextY < -CLOWN_SIZE * 2) {
      velocity.y *= -1;
    }

    position.x += velocity.x * frames;
    position.y += velocity.y * frames;
    placeClown();
    requestAnimationFrame(animate);
  }

  requestAnimationFrame(animate);
}
