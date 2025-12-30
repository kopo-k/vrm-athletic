<?php
declare(strict_types=1);

namespace App\Controller;

/**
 * Messageboard Controller
 *
 * @property \App\Model\Table\MessageboardTable $Messageboard
 */
class MessageboardController extends AppController
{
    /**
     * Index method
     *
     * @return \Cake\Http\Response|null|void Renders view
     */
    public function index()
    {
        $query = $this->Messageboard->find();
        $messageboard = $this->paginate($query);

        $this->set(compact('messageboard'));
    }

    /**
     * View method
     *
     * @param string|null $id Messageboard id.
     * @return \Cake\Http\Response|null|void Renders view
     * @throws \Cake\Datasource\Exception\RecordNotFoundException When record not found.
     */
    public function view($id = null)
    {
        $messageboard = $this->Messageboard->get($id, contain: []);
        $this->set(compact('messageboard'));
    }

    /**
     * Add method
     *
     * @return \Cake\Http\Response|null|void Redirects on successful add, renders view otherwise.
     */
    public function add()
    {
        $messageboard = $this->Messageboard->newEmptyEntity();
        if ($this->request->is('post')) {
            $messageboard = $this->Messageboard->patchEntity($messageboard, $this->request->getData());
            if ($this->Messageboard->save($messageboard)) {
                $this->Flash->success(__('The messageboard has been saved.'));

                return $this->redirect(['action' => 'index']);
            }
            $this->Flash->error(__('The messageboard could not be saved. Please, try again.'));
        }
        $this->set(compact('messageboard'));
    }

    /**
     * Edit method
     *
     * @param string|null $id Messageboard id.
     * @return \Cake\Http\Response|null|void Redirects on successful edit, renders view otherwise.
     * @throws \Cake\Datasource\Exception\RecordNotFoundException When record not found.
     */
    public function edit($id = null)
    {
        $messageboard = $this->Messageboard->get($id, contain: []);
        if ($this->request->is(['patch', 'post', 'put'])) {
            $messageboard = $this->Messageboard->patchEntity($messageboard, $this->request->getData());
            if ($this->Messageboard->save($messageboard)) {
                $this->Flash->success(__('The messageboard has been saved.'));

                return $this->redirect(['action' => 'index']);
            }
            $this->Flash->error(__('The messageboard could not be saved. Please, try again.'));
        }
        $this->set(compact('messageboard'));
    }

    /**
     * Delete method
     *
     * @param string|null $id Messageboard id.
     * @return \Cake\Http\Response|null Redirects to index.
     * @throws \Cake\Datasource\Exception\RecordNotFoundException When record not found.
     */
    public function delete($id = null)
    {
        $this->request->allowMethod(['post', 'delete']);
        $messageboard = $this->Messageboard->get($id);
        if ($this->Messageboard->delete($messageboard)) {
            $this->Flash->success(__('The messageboard has been deleted.'));
        } else {
            $this->Flash->error(__('The messageboard could not be deleted. Please, try again.'));
        }

        return $this->redirect(['action' => 'index']);
    }
}
