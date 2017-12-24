using System.Collections;
using System.Collections.Generic;
using UnityEngine; using UnityEngine.Assertions;

public class ParticleSpreader : MonoBehaviour
{
	private IParticleDriver driver;
	void Awake() { this.driver = this.GetComponent<IParticleDriver>(); Assert.IsNotNull(this.driver); }
	[SerializeField] private int spawnRate = 10;
	void FixedUpdate() { this.driver.Spawn(this.spawnRate, Vector3.zero, 1); }
	// void FixedUpdate() { for(int i = 0; i < this.spawnRate; i++) this.driver.Spawn(Vector3.zero, 1); }
}
