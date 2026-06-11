const circleMath = require("./circle-math");

function calculateBounceDirection(ballDir, defenderPaddleAngle, impactAngle, paddleArcDegrees, paddleAimInfluence) {
  const impactDirection = circleMath.angleToDirection(impactAngle);
  const paddleDirection = circleMath.angleToDirection(defenderPaddleAngle);
  const dot = ballDir.x * paddleDirection.x + ballDir.y * paddleDirection.y;
  let reflectedX = ballDir.x - 2 * dot * paddleDirection.x;
  let reflectedY = ballDir.y - 2 * dot * paddleDirection.y;

  const offset = circleMath.deltaAngle(defenderPaddleAngle, impactAngle) / Math.max(1, paddleArcDegrees * 0.5);
  const tangent = { x: -paddleDirection.y, y: paddleDirection.x };
  let aimedX = reflectedX + tangent.x * offset * paddleAimInfluence;
  let aimedY = reflectedY + tangent.y * offset * paddleAimInfluence;
  let length = Math.hypot(aimedX, aimedY) || 1;
  aimedX /= length;
  aimedY /= length;

  const antiImpactDot = aimedX * -impactDirection.x + aimedY * -impactDirection.y;
  if (antiImpactDot < 0.15) {
    aimedX = circleMath.lerp(aimedX, -impactDirection.x, 0.5);
    aimedY = circleMath.lerp(aimedY, -impactDirection.y, 0.5);
    length = Math.hypot(aimedX, aimedY) || 1;
    aimedX /= length;
    aimedY /= length;
  }

  return { x: aimedX, y: aimedY };
}

function randomDirection() {
  const angle = Math.random() * 360;
  return circleMath.angleToDirection(angle);
}

module.exports = {
  calculateBounceDirection,
  randomDirection
};
