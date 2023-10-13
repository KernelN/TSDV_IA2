using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public GameObject agent;

    private static CameraFollow instance = null;

    public static CameraFollow Instance
    {
        get
        {
            if (instance == null)
                instance = FindObjectOfType<CameraFollow>();

            return instance;
        }
    }

    private void Awake()
    {
        instance = this;
    }

    public void Reset()
    {
        this.transform.position = new Vector3(0, 0, this.transform.position.z);
    }

    public void UpdateCamera()
    {
        Vector3 follow = Vector3.zero;

        if (PopulationManager.Instance != null)
        {
            if (PopulationManager.Instance.GetBestAgent() != null)
                follow = PopulationManager.Instance.GetBestAgent().transform.position;
            else
                return;
        }
        else
            follow = agent.transform.position;

        Vector3 pos = this.transform.position;
        pos.x = follow.x;
        this.transform.position = pos;
    }
}
