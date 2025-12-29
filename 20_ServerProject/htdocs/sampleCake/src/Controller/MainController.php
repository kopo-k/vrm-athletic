<?php
declare(strict_types=1);

namespace App\Controller;

/**
 * Main Controller
 *
 */
class MainController extends AppController
{
    /**
     * Index method
     *
     * @return \Cake\Http\Response|null|void Renders view
     */
    public function index()
    {
        $name_title = "ハローワールドproject";
        $this->set('view_name_title', $name_title);

    }

}