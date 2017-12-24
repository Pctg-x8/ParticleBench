using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(ParticleSpreader))]
public class ExecutorPrewarm : MonoBehaviour
{
	private ParticleSpreader spreader;
	void Awake() { this.spreader = this.GetComponent<ParticleSpreader>(); }
	void Update()
	{
		if(Input.GetButtonDown("Fire1"))
		{
			this.spreader.enabled = !this.spreader.enabled;
		}
	}
}
