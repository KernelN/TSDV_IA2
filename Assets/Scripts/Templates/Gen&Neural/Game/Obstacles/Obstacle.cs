using UnityEngine;

public class Obstacle : MonoBehaviour
{
    public System.Action<Obstacle> OnDestroy;
    public Vector2 holeSize;
    
    public void CheckToDestroy()
    {
        if (this.transform.position.x - Camera.main.transform.position.x < -7.5f)
        {
            if (OnDestroy != null)
                OnDestroy.Invoke(this);

            Destroy(this.gameObject);
        }

    }
}