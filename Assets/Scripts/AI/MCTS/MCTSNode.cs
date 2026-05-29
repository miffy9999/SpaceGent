using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  MCTSNode — 탐색 트리 노드.
//  각 노드는 "이 액션을 취해서 도달한 상태" 를 의미.
//  Root는 actionFromParent = -1 (시작 상태).
// =====================================================================
public class MCTSNode
{
    public MCTSNode parent;
    public List<MCTSNode> children = new List<MCTSNode>();
    public List<int> untriedActions;       // 아직 확장 안 된 액션들
    public int actionFromParent;           // 이 노드로 오게 된 카드 인덱스
    public int playerWhoMoved;             // 그 액션을 취한 플레이어 (협력 게임이라 큰 의미는 없지만 디버그용)

    public int   visits = 0;
    public float totalReward = 0f;

    public MCTSNode(MCTSNode parent, int actionFromParent, int playerWhoMoved, List<int> legalActions)
    {
        this.parent = parent;
        this.actionFromParent = actionFromParent;
        this.playerWhoMoved   = playerWhoMoved;
        this.untriedActions   = new List<int>(legalActions);
    }

    public bool IsFullyExpanded() => untriedActions.Count == 0;
    public bool IsLeaf()          => children.Count == 0;

    // ---------------------------------------------------------------
    // UCB1로 best child 선택
    //   score = mean_reward + c * sqrt(ln(parent.visits) / child.visits)
    //   협력 게임이므로 우리(assignee)의 reward 관점에서 최대화.
    // ---------------------------------------------------------------
    public MCTSNode SelectChildUCB(float explorationC)
    {
        MCTSNode best = null;
        float bestScore = float.NegativeInfinity;
        float logParentVisits = Mathf.Log(Mathf.Max(1, visits));

        for (int i = 0; i < children.Count; i++)
        {
            var ch = children[i];
            float exploit = ch.totalReward / Mathf.Max(1, ch.visits);
            float explore = explorationC * Mathf.Sqrt(logParentVisits / Mathf.Max(1, ch.visits));
            float score = exploit + explore;
            if (score > bestScore) { bestScore = score; best = ch; }
        }
        return best;
    }

    // ---------------------------------------------------------------
    // BestAction: 최종 결정 (가장 자주 방문된 child)
    //   협력 게임에선 mean reward도 좋지만, visits 기준이 더 안정적.
    // ---------------------------------------------------------------
    public int BestActionByVisits()
    {
        int bestAction = -1;
        int bestVisits = -1;
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i].visits > bestVisits)
            {
                bestVisits = children[i].visits;
                bestAction = children[i].actionFromParent;
            }
        }
        return bestAction;
    }

    // ---------------------------------------------------------------
    // 보상 backpropagation (root까지)
    // ---------------------------------------------------------------
    public void Backpropagate(float reward)
    {
        MCTSNode n = this;
        while (n != null)
        {
            n.visits++;
            n.totalReward += reward;
            n = n.parent;
        }
    }
}
