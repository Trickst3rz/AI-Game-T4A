﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Agent : MonoBehaviour
{
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 velocity;
    public Vector3 acceleration;
    public Vector3 behind;
    public GameObject leaderGameObject;

    public Level level;
    public AgentConfig config;

    private Vector3 wanderTarget;
    public bool isLeader = false;
    private Agent leader;

    private void Start()
    {

        level = FindObjectOfType<Level>();
        config = FindObjectOfType<AgentConfig>();

        if (leaderGameObject == null && this.name != "Leader")
        {
            leaderGameObject = GameObject.Find("Leader");
            leader = leaderGameObject.GetComponent<Agent>();
            leader.isLeader = true;
        }
        
        position = transform.position;
        rotation = transform.rotation;
        velocity = new Vector3(Random.Range(-3, 3), Random.Range(-3, 3), 0);
    }

    void FixedUpdate()
    {
        if (isLeader)
        {
            //RaycastCollison();
            //acceleration = CombineWander();
            acceleration = Vector3.ClampMagnitude(acceleration, config.maxAcceleration);
            velocity = velocity + acceleration * Time.deltaTime;
            velocity = Vector3.ClampMagnitude(velocity, config.maxVelocity);
            position = position + velocity * Time.deltaTime;
            ////WrapAround(ref position, -level.bounds, level.bounds);
            transform.position = position;
            transform.rotation = rotation;
        }
        else
        {
            //RaycastCollison();
            acceleration = followLeader();
            acceleration = Vector3.ClampMagnitude(acceleration, config.maxAcceleration);
            velocity = velocity + acceleration * Time.deltaTime;
            velocity = Vector3.ClampMagnitude(velocity, config.maxVelocity);
            position = position + velocity * Time.deltaTime;
            //WrapAround(ref position, -level.bounds, level.bounds);
            transform.position = position;
            transform.rotation = rotation;

        }

    }

    void RaycastCollison()
    {
        Ray2D ray2D = new Ray2D(transform.position, velocity);
        ContactFilter2D filter;
        filter.layerMask = 1 << 8;
        RaycastHit2D hit = Physics2D.Raycast(transform.position, transform.position + velocity);

        int iterationCount = 6;
        float angleMin = 180 / iterationCount;

        if (hit.collider != null)
        {
            Vector3 tempVelocity = velocity;
            Debug.DrawRay(transform.position, transform.position + velocity, Color.red);
            for (int i = 0; i < iterationCount; i++)
            {
                for (int d = -1; d < 2; d += 2)
                {//Normalise this later on to make it more smooth
                    velocity = (Quaternion.Euler(angleMin * i * d, 0, 0) * tempVelocity) * hit.distance;
                    if (hit.collider == null)
                        return;
                }
            }

            Debug.Log("hit");
        }
        else
        {
            Debug.DrawRay(transform.position, transform.position + velocity, Color.green);
        }
    }

    Vector3 followLeader()
    {
        Vector3 tv = leader.velocity;
        Vector3 force = new Vector3();

        //Calculate the ahead point
        Vector3.Normalize(tv);
        tv.Scale(new Vector3(config.LEADER_AHEAD_DIST, config.LEADER_AHEAD_DIST, 0));
        Vector3 ahead = leader.position + tv;

        //Calculate the behind point
        tv.Scale(new Vector3(config.LEADER_BEHIND_DIST, config.LEADER_BEHIND_DIST, 0));
        behind = leader.position + tv;

        if (isOnLeaderSight(leader, ahead))
            force = force + Evade(leader);

        force = force + Arrival(behind);

        //force = force + Cohesion() * config.cohesionPriority + Alignment() * config.alignmentPriority + Separation() * config.separationPriority;

        //Look at adding alignment and cohesion

        force = force + Separation() * config.separationPriority;

        return force;
    }

    protected Vector3 Wander()
    {
        float jitter = config.wanderJitter * Time.deltaTime;
        wanderTarget += new Vector3(RandomBinomial() * jitter, RandomBinomial() * jitter, 0);
        wanderTarget = wanderTarget.normalized;
        wanderTarget *= config.wanderRadius;
        Vector3 targetInLocalSpace = wanderTarget + new Vector3(config.wanderDistance, config.wanderDistance, 0);
        Vector3 targetInWorldSpace = transform.TransformPoint(targetInLocalSpace);
        targetInWorldSpace -= this.position;
        return targetInWorldSpace.normalized;
    }

    Vector3 Cohesion()
    {
        Vector3 cohesionVector = new Vector3();
        int countAgents = 0;
        List<Agent> neighbours = level.GetNeighbours(this, config.cohesionRadius);

        if (neighbours.Count == 0)
            return cohesionVector;

        foreach (Agent agent in neighbours)
        {
            if (isInFOV(agent.position))
            {
                cohesionVector += agent.position;
                countAgents++;
            }
        }

        if (countAgents == 0)
            return cohesionVector;

        cohesionVector /= countAgents;
        cohesionVector = cohesionVector - this.position;
        cohesionVector = Vector3.Normalize(cohesionVector);
        return cohesionVector;
    }

    Vector3 Alignment()
    {
        Vector3 alignVector = new Vector3();
        List<Agent> agents = level.GetNeighbours(this, config.alignmentRadius);

        if (agents.Count == 0)
            return alignVector;

        foreach (Agent agent in agents)
        {
            if (isInFOV(agent.position))
                alignVector += agent.velocity;
        }

        return alignVector.normalized;
    }

    Vector3 Separation()
    {
        Vector3 separationVector = new Vector3();
        List<Agent> agents = level.GetNeighbours(this, config.separationRadius);

        if (agents.Count == 0)
            return separationVector;

        foreach (Agent agent in agents)
        {
            if (isInFOV(agent.position))
            {
                Vector3 movingTowards = this.position - agent.position;
                if (movingTowards.magnitude > 0)
                {
                    separationVector += movingTowards.normalized / movingTowards.magnitude;
                }
            }
        }
        return separationVector.normalized;
    }

    Vector3 Avoidance()
    {
        Vector3 avoidVector = new Vector3();
        List<Enemy> enemyList = level.GetEnemies(this, config.avoidanceRadius);

        if (enemyList.Count == 0)
            return avoidVector;

        foreach (Enemy enemy in enemyList)
        {
            avoidVector += RunAway(enemy.position);
        }

        return avoidVector.normalized;
    }

    bool isOnLeaderSight(Agent leader, Vector3 leaderAhead)
    {
        return Vector3.Distance(leaderAhead, transform.position) <= config.LeaderSightRadius || Vector3.Distance(leader.position, transform.position) <= config.LeaderSightRadius;
    }

    Vector3 Evade(Agent agent)
    {
        Vector3 distance = agent.position - position;
        float UpdatesAhead = distance.magnitude / config.maxVelocity;
        Vector3 futurePosition = agent.position + agent.velocity * UpdatesAhead;
        return RunAway(futurePosition);
    }

    Vector3 Arrival(Vector3 target)
    {
        Vector3 desiredVelocity = target - position;
        float distance = desiredVelocity.magnitude;

        if (distance < config.slowingRadius)
        {
            desiredVelocity = desiredVelocity.normalized * config.maxVelocity * (distance / config.slowingRadius);
        }
        else
        {
            desiredVelocity = desiredVelocity.normalized * config.maxVelocity;
        }

        return desiredVelocity - velocity;
    }

    Vector3 RunAway(Vector3 target)
    {
        Vector3 neededVelocity = (position - target).normalized * config.maxVelocity;
        return neededVelocity - velocity;
    }

    virtual protected Vector3 CombineWander()
    {
        Vector3 finalVec = config.cohesionPriority * Cohesion() + config.wanderPriority * Wander() + config.alignmentPriority * Alignment() + config.separationPriority * Separation() + config.avoidancePriority * Avoidance();
        return finalVec;
    }

    virtual protected Vector3 CombineFollow()
    {
        Vector3 finalVec = config.cohesionPriority * Cohesion() + followLeader() + config.alignmentPriority * Alignment() + config.separationPriority * Separation() + config.avoidancePriority * Avoidance();
        return finalVec;
    }

    void WrapAround(ref Vector3 vector, float min, float max)
    {
        vector.x = WrapAroundFloat(vector.x, min, max);
        vector.y = WrapAroundFloat(vector.y, min, max);
        vector.z = WrapAroundFloat(vector.z, min, max);
    }

    float WrapAroundFloat(float value, float min, float max)
    {
        if (value > max)
            value = min;
        else if (value < min)
            value = max;
        return value;
    }

    float RandomBinomial()
    {
        return Random.Range(0f, 1f) - Random.Range(0f, 1f);
    }

    bool isInFOV(Vector3 vec)
    {
        return Vector3.Angle(this.velocity, vec - this.position) <= config.maxFOV;
    }
}